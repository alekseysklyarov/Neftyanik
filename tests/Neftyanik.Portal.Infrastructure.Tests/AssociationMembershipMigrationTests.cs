using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class AssociationMembershipMigrationTests
{
    private const string Previous = "20260915193703_AddAssociationFoundation";
    private const string Current = "20260918151504_AddAssociationMemberships";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration_PreservesEveryLegacyColumnAndBackfillsExactPermissions(bool script)
    {
        var associationContext = new AssociationContext();
        await using var database = new ApplicationDbContext(AssociationDatabaseFixture.CreateOptions(), associationContext);
        try
        {
            var migrator = database.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);
            var original = await database.Associations.SingleAsync();
            original.Slug = "original-association";
            var neftyanik = new Association { Slug = "neftyanik", Name = "Neftyanik" };
            database.Associations.Add(neftyanik);
            await database.SaveChangesAsync();
            Assert.NotEqual(original.Id, neftyanik.Id);
            associationContext.Resolve(neftyanik);

            foreach (var id in new[] { "admin", "accountant", "member", "multi", "inactive", "unassigned" })
            {
                var user = new ApplicationUser { Id = id, UserName = id, FirstName = id, LastName = "Legacy", IsActive = id != "inactive", SecurityStamp = "preserve-" + id };
                user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, "Legacy123!");
                database.Users.Add(user);
            }
            var roles = await database.Roles.ToDictionaryAsync(x => x.Name!, x => x.Id);
            var assignments = new[]
            {
                ("admin", RoleNames.Administrator), ("accountant", RoleNames.Accountant), ("member", RoleNames.Member),
                ("multi", RoleNames.Member), ("multi", RoleNames.Accountant), ("inactive", RoleNames.Member)
            };
            database.UserRoles.AddRange(assignments.Select(x => new IdentityUserRole<string> { UserId = x.Item1, RoleId = roles[x.Item2] }));
            database.AddRange(AssociationFoundationTests.CreateBusinessGraph(neftyanik.Id, "member").Cast<object>());
            database.UserLoginHistories.AddRange(new UserLoginHistory { UserId = "member", LoggedInAtUtc = DateTimeOffset.UtcNow.AddYears(-1) },
                new UserLoginHistory { UserId = "admin", LoggedInAtUtc = DateTimeOffset.UtcNow },
                new UserLoginHistory { UserId = "unassigned", LoggedInAtUtc = DateTimeOffset.UtcNow });
            await database.SaveChangesAsync();
            await database.Database.OpenConnectionAsync();
            var before = new Dictionary<string, (string Columns, string Order, string Json)>();
            foreach (var entity in database.Model.GetEntityTypes().Where(x => x.ClrType != typeof(AssociationUserMembership) && x.ClrType != typeof(AssociationLoginEvent)))
            {
                var table = entity.GetTableName()!;
                var columns = string.Join(",", entity.GetProperties().Select(x => $"[{x.GetColumnName()}]"));
                var order = string.Join(",", entity.FindPrimaryKey()!.Properties.Select(x => $"[{x.GetColumnName()}]"));
                before.Add(table, (columns, order, await AssociationMigrationTests.ReadJsonAsync(database, table, columns, order)));
            }
            if (script)
            {
                var sql = migrator.GenerateScript(Previous, Current, MigrationsSqlGenerationOptions.Idempotent);
                Assert.Contains("[Slug] = N'neftyanik'", sql);
                Assert.DoesNotContain("UPDATE [AspNetUsers]", sql);
                await AssociationMigrationTests.ExecuteScriptAsync(database, sql);
                await AssociationMigrationTests.ExecuteScriptAsync(database, sql);
            }
            else
            {
                await migrator.MigrateAsync(Current);
                await migrator.MigrateAsync(Current);
            }
            foreach (var (table, snapshot) in before)
            {
                Assert.Equal(snapshot.Json, await AssociationMigrationTests.ReadJsonAsync(database, table, snapshot.Columns, snapshot.Order));
            }
            var memberships = await database.AssociationUserMemberships.AsNoTracking().ToListAsync();
            Assert.Equal(assignments.Length, memberships.Count);
            Assert.All(memberships, x => Assert.Equal(neftyanik.Id, x.AssociationId));
            foreach (var (userId, role) in assignments)
            {
                var membership = Assert.Single(memberships.Where(x => x.ApplicationUserId == userId && x.Role == role));
                Assert.Equal(userId != "inactive", membership.IsActive);
            }
            Assert.Equal(3, await database.UserLoginHistories.CountAsync());
            Assert.Empty(await database.AssociationLoginEvents.IgnoreQueryFilters().ToListAsync());
            var activity = await new UserActivityService(database, TimeProvider.System).GetDashboardSummaryAsync();
            Assert.Equal(5, activity.TotalRegisteredUsers);
            Assert.Equal(0, activity.EverLoggedInUsers);
            var authorization = new AssociationMembershipService(database);
            Assert.Equal(new[] { RoleNames.Administrator }, await authorization.GetRolesAsync("admin"));
            Assert.Equal(new[] { RoleNames.Accountant, RoleNames.Member }, (await authorization.GetRolesAsync("multi")).OrderBy(x => x));
            Assert.Empty(await authorization.GetRolesAsync("inactive"));
            Assert.Empty(await authorization.GetRolesAsync("unassigned"));
            var history = (await database.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.Equal(Current, history.Last());
            await Assert.ThrowsAsync<NotSupportedException>(() => migrator.MigrateAsync(Previous));
            Assert.Equal(assignments.Length, await database.AssociationUserMemberships.CountAsync());
        }
        finally
        {
            await database.Database.CloseConnectionAsync();
            await database.Database.EnsureDeletedAsync();
        }
    }

    [Theory]
    [InlineData("missing", 51030)]
    [InlineData("unknown-role", 51031)]
    [InlineData("role-claim", 51032)]
    [InlineData("linked-without-role", 51033)]
    [InlineData("foreign-link", 51034)]
    [InlineData("duplicate-link", 51035)]
    public async Task Migration_AmbiguousLegacyDataAbortsWithoutChangingData(string scenario, int errorNumber)
    {
        var associationContext = new AssociationContext();
        await using var database = new ApplicationDbContext(AssociationDatabaseFixture.CreateOptions(), associationContext);
        try
        {
            var migrator = database.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);
            var association = await database.Associations.SingleAsync();
            if (scenario == "missing") association.Slug = "renamed";
            if (scenario == "foreign-link")
            {
                association = new Association { Slug = "other", Name = "Other" };
                database.Associations.Add(association);
                await database.SaveChangesAsync();
            }
            associationContext.Resolve(association);
            database.Users.Add(new ApplicationUser { Id = "legacy", UserName = "legacy", PasswordHash = "unchanged", FirstName = "Legacy", LastName = "User" });
            if (scenario == "unknown-role")
            {
                database.Roles.Add(new IdentityRole { Id = "unknown", Name = "Owner", NormalizedName = "OWNER" });
                database.UserRoles.Add(new IdentityUserRole<string> { UserId = "legacy", RoleId = "unknown" });
            }
            else if (scenario != "linked-without-role")
            {
                var roleId = await database.Roles.Where(x => x.Name == RoleNames.Member).Select(x => x.Id).SingleAsync();
                database.UserRoles.Add(new IdentityUserRole<string> { UserId = "legacy", RoleId = roleId });
            }
            if (scenario == "role-claim") database.UserClaims.Add(new IdentityUserClaim<string> { UserId = "legacy", ClaimType = System.Security.Claims.ClaimTypes.Role, ClaimValue = RoleNames.Administrator });
            if (scenario.Contains("link")) database.Members.Add(new Member { ApplicationUserId = "legacy", FullName = "Legacy" });
            if (scenario == "duplicate-link") database.Members.Add(new Member { ApplicationUserId = "legacy", FullName = "Duplicate" });
            await database.SaveChangesAsync();
            var exception = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(Current));
            Assert.Equal(errorNumber, exception.Number);
            Assert.Contains("Stage 3:", exception.Message);
            Assert.Equal("unchanged", await database.Users.Select(x => x.PasswordHash).SingleAsync());
            Assert.DoesNotContain(Current, await database.Database.GetAppliedMigrationsAsync());
            Assert.Equal(Previous, (await database.Database.GetAppliedMigrationsAsync()).Last());
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }
}
