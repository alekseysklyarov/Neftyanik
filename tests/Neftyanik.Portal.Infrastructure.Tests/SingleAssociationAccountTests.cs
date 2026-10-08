using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class SingleAssociationAccountTests
{
    [Fact]
    public async Task DirectSql_EnforcesBothCompositeForeignKeysAndAllowsMultipleLocalRoles()
    {
        var fixture = new AssociationDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            await using var db = fixture.CreateContext();
            var user = new ApplicationUser { UserName = "sql-only" };
            var second = new Association { Slug = "sql-second", Name = "Second" };
            db.AddRange(user, second);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AssociationAccountBindings (ApplicationUserId, AssociationId) VALUES ({user.Id}, {db.CurrentAssociationId})");
            foreach (var role in new[] { RoleNames.Member, RoleNames.Accountant, RoleNames.Administrator })
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AssociationUserMemberships (ApplicationUserId, AssociationId, Role, IsActive, CreatedAtUtc) VALUES ({user.Id}, {db.CurrentAssociationId}, {role}, 1, SYSDATETIMEOFFSET())");
            Assert.Equal(3, await db.AssociationUserMemberships.CountAsync(x => x.ApplicationUserId == user.Id));
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Members (ApplicationUserId, AssociationId, FullName, CreatedAtUtc) VALUES ({user.Id}, {db.CurrentAssociationId}, 'SQL member', SYSUTCDATETIME())");
            var membershipError = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AssociationUserMemberships (ApplicationUserId, AssociationId, Role, IsActive, CreatedAtUtc) VALUES ({user.Id}, {second.Id}, 'Member', 0, SYSDATETIMEOFFSET())"));
            Assert.Equal(547, membershipError.Number);
            var memberError = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Members (ApplicationUserId, AssociationId, FullName, CreatedAtUtc) VALUES ({user.Id}, {second.Id}, 'Foreign member', SYSUTCDATETIME())"));
            Assert.Equal(547, memberError.Number);
            Assert.Contains("FK_Members_AssociationAccountBindings", memberError.Message);
            var duplicate = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AssociationAccountBindings (ApplicationUserId, AssociationId) VALUES ({user.Id}, {second.Id})"));
            Assert.Contains(duplicate.Number, new[] { 2601, 2627 });
            var key = db.Model.FindEntityType(typeof(AssociationAccountBinding))!.GetKeys();
            Assert.Contains(key, x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "ApplicationUserId", "AssociationId" }));
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentIdentityAndTenantAssignment_UsesSameLockUntilCommit(bool platformFirst)
    {
        var fixture = new AssociationDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            await using var seed = fixture.CreateContext();
            var user = new ApplicationUser { UserName = "racing-user" };
            seed.Users.Add(user);
            seed.Roles.Add(new IdentityRole(RoleNames.PlatformAdministrator) { NormalizedName = "PLATFORMADMINISTRATOR" });
            await seed.SaveChangesAsync();
            var observer = new AssignmentLockObserver();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped<IAssociationContext>(_ => TestAssociations.Neftyanik);
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(seed.Database.GetConnectionString()).AddInterceptors(observer));
            services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
            await using var provider = services.BuildServiceProvider();
            await using var firstScope = provider.CreateAsyncScope();
            var first = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await first.Database.BeginTransactionAsync();
            await AssignAsync(firstScope.ServiceProvider, platformFirst, user.Id);
            observer.Enabled = true;
            var competing = Task.Run(async () =>
            {
                await using var secondScope = provider.CreateAsyncScope();
                await AssignAsync(secondScope.ServiceProvider, !platformFirst, user.Id);
            });
            await observer.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(competing.IsCompleted);
            await transaction.CommitAsync();
            await Assert.ThrowsAsync<AssociationIsolationException>(() => competing.WaitAsync(TimeSpan.FromSeconds(20)));
            await using var verify = fixture.CreateContext();
            var bound = await verify.AssociationAccountBindings.AnyAsync(x => x.ApplicationUserId == user.Id);
            var global = await verify.UserRoles.AnyAsync(x => x.UserId == user.Id);
            Assert.NotEqual(bound, global);
            Assert.Equal(platformFirst, global);
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Fact]
    public async Task Migration_NeftyanikPreservesIdentityAndEveryExistingFinancialColumn()
    {
        var fixture = new AssociationDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            await using var db = fixture.CreateContext();
            var user = new ApplicationUser { UserName = "legacy-finance", Email = "legacy@example.invalid", SecurityStamp = "preserve-stamp", MustChangePassword = true };
            user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)));
            db.Users.Add(user);
            db.AssociationUserMemberships.AddRange(
                new AssociationUserMembership { ApplicationUser = user, Role = RoleNames.Member },
                new AssociationUserMembership { ApplicationUser = user, Role = RoleNames.Accountant, IsActive = false });
            db.AddRange(AssociationFoundationTests.CreateBusinessGraph(db.CurrentAssociationId, user.Id));
            await db.SaveChangesAsync();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260922175600_AddPlatformAssociationManagement");
            await db.Database.OpenConnectionAsync();
            var snapshots = new Dictionary<string, (string Columns, string Order, string Json)>();
            foreach (var entity in db.Model.GetEntityTypes().Where(x => x.ClrType != typeof(AssociationAccountBinding)))
            {
                var table = entity.GetTableName()!;
                var actualColumns = await db.Database.SqlQueryRaw<string>(
                    "SELECT COLUMN_NAME AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = {0}", table).ToListAsync();
                var columns = string.Join(",", entity.GetProperties().Where(x => actualColumns.Contains(x.GetColumnName()!)).Select(x => $"[{x.GetColumnName()}]"));
                var order = string.Join(",", entity.FindPrimaryKey()!.Properties.Select(x => $"[{x.GetColumnName()}]"));
                snapshots.Add(table, (columns, order, await AssociationMigrationTests.ReadJsonAsync(db, table, columns, order)));
            }
            await migrator.MigrateAsync();
            foreach (var (table, snapshot) in snapshots)
                Assert.Equal(snapshot.Json, await AssociationMigrationTests.ReadJsonAsync(db, table, snapshot.Columns, snapshot.Order));
            Assert.Equal(db.CurrentAssociationId, (await db.AssociationAccountBindings.AsNoTracking().SingleAsync(x => x.ApplicationUserId == user.Id)).AssociationId);
        }
        finally { await fixture.DisposeAsync(); }
    }

    private static async Task AssignAsync(IServiceProvider services, bool platform, string userId)
    {
        if (platform)
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.AddToRoleAsync((await users.FindByIdAsync(userId))!, RoleNames.PlatformAdministrator)).Succeeded);
        }
        else
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = userId, Role = RoleNames.Administrator });
            await db.SaveChangesAsync();
        }
    }

    private sealed class AssignmentLockObserver : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("sys.sp_getapplock", StringComparison.Ordinal)) Arrived.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Membership_RejectsSecondAssociationIncludingInactiveMembership(bool active)
    {
        var fixture = new AssociationDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            await using var first = fixture.CreateContext();
            var user = new ApplicationUser { UserName = "single-account" };
            var second = new Association { Slug = "second", Name = "Second" };
            first.AddRange(user, second);
            first.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUser = user, Role = RoleNames.Member, IsActive = active });
            await first.SaveChangesAsync();
            first.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = user.Id, Role = RoleNames.Accountant });
            await first.SaveChangesAsync();
            Assert.Equal(2, await first.AssociationUserMemberships.CountAsync(x => x.ApplicationUserId == user.Id));
            await using var other = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(first.Database.GetConnectionString()).Options, TestAssociations.Resolved(second.Id, second.Slug));
            other.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = user.Id, Role = RoleNames.Administrator });
            await Assert.ThrowsAsync<AssociationIsolationException>(() => other.SaveChangesAsync());
            other.ChangeTracker.Clear();
            other.Members.Add(new Member { ApplicationUserId = user.Id, FullName = "Wrong association" });
            await Assert.ThrowsAsync<AssociationIsolationException>(() => other.SaveChangesAsync());

            var sqlError = await Assert.ThrowsAsync<SqlException>(() => first.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AssociationUserMemberships (ApplicationUserId, AssociationId, Role, IsActive, CreatedAtUtc) VALUES ({user.Id}, {second.Id}, 'Member', 0, SYSDATETIMEOFFSET())"));
            Assert.Equal(547, sqlError.Number);
            Assert.Contains("FK_AssociationUserMemberships_AssociationAccountBindings", sqlError.Message);
            var bindingError = await Assert.ThrowsAsync<SqlException>(() => first.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AssociationAccountBindings (ApplicationUserId, AssociationId) VALUES ({user.Id}, {second.Id})"));
            Assert.Contains(bindingError.Number, new[] { 2601, 2627 });

            first.AssociationUserMemberships.RemoveRange(await first.AssociationUserMemberships.Where(x => x.ApplicationUserId == user.Id).ToListAsync());
            await first.SaveChangesAsync();
            Assert.True(await first.AssociationAccountBindings.AnyAsync(x => x.ApplicationUserId == user.Id));
            other.ChangeTracker.Clear();
            other.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = user.Id, Role = RoleNames.Member });
            await Assert.ThrowsAsync<AssociationIsolationException>(() => other.SaveChangesAsync());
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlatformAdministrator_CannotHaveTenantBindingInEitherAssignmentOrder(bool platformFirst)
    {
        var fixture = new AssociationDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            await using var db = fixture.CreateContext();
            var user = new ApplicationUser { UserName = "global" };
            var role = new IdentityRole(RoleNames.PlatformAdministrator) { NormalizedName = "PLATFORMADMINISTRATOR" };
            db.AddRange(user, role);
            await db.SaveChangesAsync();
            var membership = new AssociationUserMembership { ApplicationUserId = user.Id, Role = RoleNames.Administrator };
            var globalRole = new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id };
            if (platformFirst) db.UserRoles.Add(globalRole); else db.AssociationUserMemberships.Add(membership);
            await db.SaveChangesAsync();
            if (platformFirst) db.AssociationUserMemberships.Add(membership); else db.UserRoles.Add(globalRole);
            await Assert.ThrowsAsync<AssociationIsolationException>(() => db.SaveChangesAsync());
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Theory]
    [InlineData("membership", 51020)]
    [InlineData("member", 51020)]
    [InlineData("platform", 51021)]
    [InlineData("platform-member", 51021)]
    [InlineData("valid", 0)]
    [InlineData("multiple-roles", 0)]
    [InlineData("member-only", 0)]
    public async Task Migration_BackfillsOnlyUnambiguousAccounts(string scenario, int expectedError)
    {
        const string previous = "20260922175600_AddPlatformAssociationManagement";
        var fixture = new AssociationDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            await using var db = fixture.CreateContext();
            var user = new ApplicationUser { UserName = "legacy" };
            var second = new Association { Slug = "legacy-second", Name = "Second" };
            db.AddRange(user, second);
            db.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUser = user, Role = RoleNames.Member, IsActive = false });
            await db.SaveChangesAsync();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(previous);
            if (scenario == "multiple-roles")
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AssociationUserMemberships (ApplicationUserId, AssociationId, Role, IsActive, CreatedAtUtc) VALUES ({user.Id}, {db.CurrentAssociationId}, 'Accountant', 1, SYSDATETIMEOFFSET())");
            if (scenario is "member-only" or "platform-member")
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AssociationUserMemberships WHERE ApplicationUserId = {user.Id}");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Members (ApplicationUserId, AssociationId, FullName, CreatedAtUtc) VALUES ({user.Id}, {db.CurrentAssociationId}, 'Unambiguous member', SYSUTCDATETIME())");
            }
            if (scenario == "membership")
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AssociationUserMemberships (ApplicationUserId, AssociationId, Role, IsActive, CreatedAtUtc) VALUES ({user.Id}, {second.Id}, 'Administrator', 0, SYSDATETIMEOFFSET())");
            if (scenario == "member")
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Members (ApplicationUserId, AssociationId, FullName, CreatedAtUtc) VALUES ({user.Id}, {second.Id}, 'Legacy', SYSUTCDATETIME())");
            if (scenario is "platform" or "platform-member")
            {
                await db.Database.ExecuteSqlRawAsync("INSERT INTO AspNetRoles (Id, Name, NormalizedName) VALUES ('platform-test', 'PlatformAdministrator', 'PLATFORMADMINISTRATOR')");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AspNetUserRoles (UserId, RoleId) VALUES ({user.Id}, 'platform-test')");
            }
            if (expectedError != 0)
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync());
                Assert.Equal(expectedError, error.Number);
                Assert.Equal(previous, (await db.Database.GetAppliedMigrationsAsync()).Last());
                Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.tables WHERE name = 'AssociationAccountBindings'").SingleAsync());
                Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.indexes WHERE name = 'IX_Members_ApplicationUserId'").SingleAsync());
            }
            else
            {
                await migrator.MigrateAsync();
                Assert.Equal(db.CurrentAssociationId, (await db.AssociationAccountBindings.SingleAsync(x => x.ApplicationUserId == user.Id)).AssociationId);
                if (scenario == "member-only") Assert.False(await db.AssociationUserMemberships.AnyAsync(x => x.ApplicationUserId == user.Id));
                else Assert.False((await db.AssociationUserMemberships.SingleAsync(x => x.ApplicationUserId == user.Id && x.Role == RoleNames.Member)).IsActive);
                if (scenario == "multiple-roles") Assert.Equal(2, await db.AssociationUserMemberships.CountAsync(x => x.ApplicationUserId == user.Id));
            }
        }
        finally { await fixture.DisposeAsync(); }
    }
}
