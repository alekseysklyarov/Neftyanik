using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Identity;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public sealed class PlatformLegacyInitializationTests
{
    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA1!";

    [Fact]
    public async Task Initialize_ReviewedTenantInstallation_CreatesOnePendingUserAndPreservesEveryExistingTableRow()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddSecondAssociationAsync();
        var before = await fixture.SnapshotAsync();
        await fixture.ScopeAsync(async services => Assert.Equal(PlatformBootstrapResult.AlreadyProvisioned,
            await services.GetRequiredService<IPlatformAdministratorOnboarding>().BootstrapAsync("ordinary", "ordinary@example.test", Secret())));
        Assert.Equal(PlatformBootstrapResult.Created, await fixture.InitializeAsync());
        var after = await fixture.SnapshotAsync(excludeNewIdentity: true);
        Assert.Equal(before.Keys.OrderBy(x => x), after.Keys.OrderBy(x => x));
        foreach (var table in before.Keys) Assert.Equal(before[table], after[table]);
        await fixture.ScopeAsync(async services =>
        {
            var database = services.GetRequiredService<ApplicationDbContext>();
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync("operator"))!;
            Assert.True(user.IsActive);
            Assert.True(user.MustChangePassword);
            Assert.True(user.LockoutEnabled);
            Assert.False(user.EmailConfirmed);
            Assert.False(user.TwoFactorEnabled);
            Assert.Equal("operator@example.test", user.Email);
            Assert.True(await users.CheckPasswordAsync(user, fixture.Password));
            Assert.Equal(new[] { RoleNames.PlatformAdministrator }, await users.GetRolesAsync(user));
            Assert.False(await database.AssociationUserMemberships.IgnoreQueryFilters().AnyAsync(x => x.ApplicationUserId == user.Id));
            var expiry = await users.GetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider, PlatformAdministratorOnboarding.ExpiryToken);
            Assert.True(DateTimeOffset.Parse(expiry!) > DateTimeOffset.UtcNow.AddHours(23));
            var marker = await database.PlatformBootstrapStates.SingleAsync();
            Assert.Equal(PlatformBootstrapDisposition.Consumed, marker.Disposition);
            Assert.Equal("test-operator", marker.OperatorIdentity);
            Assert.Equal("CHANGE-123", marker.ApprovalReference);
            Assert.Equal(user.Id, marker.InitializedUserId);
            Assert.NotNull(marker.InitializedAtUtc);
            Assert.Equal(PlatformPasswordChangeResult.Succeeded,
                await services.GetRequiredService<IPlatformAdministratorOnboarding>().ChangePasswordAsync(user.Id, user.SecurityStamp!, fixture.Password, Secret()));
        });
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("consumed")]
    [InlineData("unknown")]
    [InlineData("user-id")]
    [InlineData("timestamp")]
    [InlineData("operator")]
    [InlineData("approval")]
    [InlineData("invalid-date")]
    [InlineData("multiple")]
    public async Task Initialize_AmbiguousOrContradictoryState_IsRefusedWithoutWrites(string state)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ScopeAsync(async services =>
        {
            var database = services.GetRequiredService<ApplicationDbContext>();
            var marker = await database.PlatformBootstrapStates.SingleAsync();
            if (state == "absent") database.PlatformBootstrapStates.Remove(marker);
            if (state == "consumed") marker.Disposition = PlatformBootstrapDisposition.Consumed;
            if (state == "unknown") marker.Disposition = (PlatformBootstrapDisposition)99;
            if (state == "user-id") marker.InitializedUserId = "deleted-user";
            if (state == "timestamp") marker.InitializedAtUtc = DateTimeOffset.UtcNow;
            if (state == "operator") marker.OperatorIdentity = "prior-operator";
            if (state == "approval") marker.ApprovalReference = "PRIOR";
            if (state == "invalid-date") marker.ConsumedAtUtc = default;
            if (state == "multiple")
            {
                await database.Database.ExecuteSqlRawAsync("ALTER TABLE [PlatformBootstrapStates] NOCHECK CONSTRAINT [CK_PlatformBootstrapStates_Singleton]");
                database.PlatformBootstrapStates.Add(new PlatformBootstrapState { Id = 2, Disposition = PlatformBootstrapDisposition.LegacyReviewRequired, ConsumedAtUtc = DateTimeOffset.UtcNow, Reason = "Invalid second marker" });
            }
            await database.SaveChangesAsync();
        });
        var before = await fixture.SnapshotAsync(includeMarker: true);
        Assert.Equal(PlatformBootstrapResult.AlreadyProvisioned, await fixture.InitializeAsync());
        var after = await fixture.SnapshotAsync(includeMarker: true);
        foreach (var table in before.Keys) Assert.Equal(before[table], after[table]);
    }

    [Fact]
    public async Task Initialize_PristineStore_IsRefusedEvenWithReviewMarker()
    {
        await using var fixture = await Fixture.CreateAsync(tenant: false);
        Assert.Equal(PlatformBootstrapResult.AlreadyProvisioned, await fixture.InitializeAsync());
        await fixture.ScopeAsync(async services =>
        {
            var database = services.GetRequiredService<ApplicationDbContext>();
            database.PlatformBootstrapStates.Add(new PlatformBootstrapState { Id = 1, Disposition = PlatformBootstrapDisposition.LegacyReviewRequired, ConsumedAtUtc = DateTimeOffset.UtcNow, Reason = "Inconsistent" });
            await database.SaveChangesAsync();
        });
        Assert.Equal(PlatformBootstrapResult.AlreadyProvisioned, await fixture.InitializeAsync());
    }

    [Theory]
    [InlineData("role")]
    [InlineData("assignment")]
    [InlineData("role-claim")]
    [InlineData("user-marker")]
    [InlineData("user-role-claim")]
    [InlineData("token")]
    public async Task Initialize_PriorPlatformEvidence_IsRefusedAndPermanentlySealed(string evidence)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ScopeAsync(async services =>
        {
            var database = services.GetRequiredService<ApplicationDbContext>();
            var user = await database.Users.SingleAsync();
            if (evidence is "role" or "assignment")
            {
                var role = new IdentityRole(RoleNames.PlatformAdministrator) { NormalizedName = "PLATFORMADMINISTRATOR" };
                database.Roles.Add(role);
                if (evidence == "assignment") database.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });
            }
            if (evidence == "role-claim") database.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = (await database.Roles.FirstAsync()).Id, ClaimType = PlatformAdministratorOnboarding.BootstrapMarker, ClaimValue = "deleted-user" });
            if (evidence is "user-marker" or "user-role-claim") database.UserClaims.Add(new IdentityUserClaim<string>
            {
                UserId = user.Id, ClaimType = evidence == "user-marker" ? PlatformAdministratorOnboarding.BootstrapMarker : "role",
                ClaimValue = evidence == "user-marker" ? "deleted-user" : RoleNames.PlatformAdministrator
            });
            if (evidence == "token") database.UserTokens.Add(new IdentityUserToken<string> { UserId = user.Id, LoginProvider = PlatformAdministratorOnboarding.TokenProvider, Name = PlatformAdministratorOnboarding.ExpiryToken, Value = "expired" });
            await database.SaveChangesAsync();
        });
        var before = await fixture.SnapshotAsync();
        Assert.Equal(PlatformBootstrapResult.AlreadyProvisioned, await fixture.InitializeAsync());
        var after = await fixture.SnapshotAsync();
        foreach (var table in before.Keys) Assert.Equal(before[table], after[table]);
        await fixture.ScopeAsync(async services => Assert.Equal(PlatformBootstrapDisposition.Consumed,
            (await services.GetRequiredService<ApplicationDbContext>().PlatformBootstrapStates.SingleAsync()).Disposition));
    }

    [Theory]
    [InlineData("TENANT", "new@example.test")]
    [InlineData("new-operator", "TENANT@EXAMPLE.TEST")]
    [InlineData("TENANT@EXAMPLE.TEST", "new@example.test")]
    public async Task Initialize_LoginOrEmailCollision_NeverChangesOrPromotesTenant(string login, string email)
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync(includeMarker: true);
        Assert.Equal(PlatformBootstrapResult.AccountExists, await fixture.InitializeAsync(login, email));
        var after = await fixture.SnapshotAsync(includeMarker: true);
        foreach (var table in before.Keys) Assert.Equal(before[table], after[table]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("contains\nnewline")]
    public async Task Initialize_InvalidApproval_IsRefused(string approval)
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(PlatformBootstrapResult.InvalidInput, await fixture.InitializeAsync(approval: approval));
        Assert.Equal(PlatformBootstrapResult.InvalidInput, await fixture.InitializeAsync(approval: new string('a', 101)));
        Assert.Equal(PlatformBootstrapResult.InvalidInput, await fixture.InitializeAsync(operatorIdentity: new string('o', 257)));
    }

    [Fact]
    public async Task Initialize_ConcurrentAttempts_CommitExactlyOneAdministrator()
    {
        await using var fixture = await Fixture.CreateAsync();
        var results = await Task.WhenAll(fixture.InitializeAsync("one", "one@example.test"), fixture.InitializeAsync("two", "two@example.test"));
        Assert.Single(results.Where(x => x == PlatformBootstrapResult.Created));
        Assert.Single(results.Where(x => x == PlatformBootstrapResult.AlreadyProvisioned));
        await fixture.ScopeAsync(async services => Assert.Equal(2, await services.GetRequiredService<ApplicationDbContext>().Users.CountAsync()));
    }

    [Theory]
    [InlineData("UPDATE [PlatformBootstrapStates]")]
    [InlineData("INSERT INTO [AspNetUserRoles]")]
    [InlineData("INSERT INTO [AspNetUserTokens]")]
    public async Task Initialize_WriteFailure_RollsBackAllProvisioningWrites(string failedStatement)
    {
        await using var fixture = await Fixture.CreateAsync(interceptor: new FailWrite(failedStatement));
        var before = await fixture.SnapshotAsync(includeMarker: true);
        Assert.Equal(PlatformBootstrapResult.Failed, await fixture.InitializeAsync());
        var after = await fixture.SnapshotAsync(includeMarker: true);
        foreach (var table in before.Keys) Assert.Equal(before[table], after[table]);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("role")]
    [InlineData("recreated-role")]
    public async Task Initialize_AfterIdentityDeletion_CannotRepeat(string deletion)
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(PlatformBootstrapResult.Created, await fixture.InitializeAsync());
        await fixture.ScopeAsync(async services =>
        {
            if (deletion == "user")
            {
                var users = services.GetRequiredService<UserManager<ApplicationUser>>();
                Assert.True((await users.DeleteAsync((await users.FindByNameAsync("operator"))!)).Succeeded);
            }
            else
            {
                var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
                Assert.True((await roles.DeleteAsync((await roles.FindByNameAsync(RoleNames.PlatformAdministrator))!)).Succeeded);
                if (deletion == "recreated-role") Assert.True((await roles.CreateAsync(new IdentityRole(RoleNames.PlatformAdministrator))).Succeeded);
            }
        });
        Assert.Equal(PlatformBootstrapResult.AlreadyProvisioned, await fixture.InitializeAsync("replacement", "replacement@example.test"));
        await fixture.ScopeAsync(async services => Assert.Equal(PlatformBootstrapResult.AlreadyProvisioned,
            await services.GetRequiredService<IPlatformAdministratorOnboarding>().BootstrapAsync("replacement", "replacement@example.test", Secret())));
    }

    [Fact]
    public async Task Initialize_ResolvedTenantContext_IsRefusedWithoutWrites()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync(includeMarker: true);
        await fixture.ScopeAsync(async services =>
        {
            var database = services.GetRequiredService<ApplicationDbContext>();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(database.Database.GetConnectionString()).Options;
            await using var tenant = new ApplicationDbContext(options, TestAssociations.Neftyanik);
            var initializer = new PlatformLegacyInitialization(tenant, services.GetRequiredService<UserManager<ApplicationUser>>(), services.GetRequiredService<RoleManager<IdentityRole>>(), TimeProvider.System);
            Assert.Equal(PlatformBootstrapResult.Failed, await initializer.InitializeAsync("operator", "operator@example.test", Secret(), "operator", "CHANGE-1"));
        });
        var after = await fixture.SnapshotAsync(includeMarker: true);
        foreach (var table in before.Keys) Assert.Equal(before[table], after[table]);
    }

    private sealed class FailWrite(string fragment) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(fragment, StringComparison.Ordinal)) throw new InvalidOperationException("Injected provisioning persistence failure.");
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly AssociationDatabaseFixture _database = new();
        private ServiceProvider _services = null!;
        public string Password { get; } = Secret();

        public static async Task<Fixture> CreateAsync(bool tenant = true, DbCommandInterceptor? interceptor = null)
        {
            var fixture = new Fixture();
            await using var database = fixture._database.CreateUnresolvedContext();
            await database.GetService<IMigrator>().MigrateAsync("20260918151504_AddAssociationMemberships");
            var connection = database.Database.GetConnectionString();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlServer(connection);
                if (interceptor is not null) options.AddInterceptors(interceptor);
            });
            services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
            services.AddScoped<IPlatformAdministratorPasswordRecovery, PlatformAdministratorPasswordRecovery>();
            services.AddScoped<IPlatformAdministratorOnboarding, PlatformAdministratorOnboarding>();
            services.AddScoped<IPlatformLegacyInitialization, PlatformLegacyInitialization>();
            fixture._services = services.BuildServiceProvider();
            if (tenant)
            {
                var user = new ApplicationUser { UserName = "tenant", Email = "tenant@example.test", EmailConfirmed = true, FirstName = "Tenant", LastName = "User", IsActive = true };
                await fixture.ScopeAsync(async provider => Assert.True((await provider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user, Secret())).Succeeded));
                await using var context = fixture._database.CreateContext();
                context.AddRange(AssociationFoundationTests.CreateBusinessGraph(1, user.Id).Cast<object>());
                context.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = user.Id, Role = RoleNames.Administrator });
                await context.SaveChangesAsync();
            }
            await database.Database.MigrateAsync();
            return fixture;
        }

        public async Task<PlatformBootstrapResult> InitializeAsync(string login = "operator", string email = "operator@example.test", string approval = "CHANGE-123", string operatorIdentity = "test-operator")
        {
            await using var scope = _services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IPlatformLegacyInitialization>().InitializeAsync(login, email, Password, operatorIdentity, approval);
        }

        public async Task ScopeAsync(Func<IServiceProvider, Task> action)
        {
            await using var scope = _services.CreateAsyncScope();
            await action(scope.ServiceProvider);
        }

        public async Task AddSecondAssociationAsync()
        {
            var user = new ApplicationUser { UserName = "other-tenant", Email = "other@example.test", FirstName = "Other", LastName = "Tenant" };
            await ScopeAsync(async services => Assert.True((await services.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user, Secret())).Succeeded));
            await using var database = _database.CreateUnresolvedContext();
            var association = new Association { Name = "Other", Slug = "other" };
            database.Associations.Add(association);
            await database.SaveChangesAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(database.Database.GetConnectionString()).Options;
            await using var tenant = new ApplicationDbContext(options, TestAssociations.Resolved(association.Id, association.Slug));
            tenant.AddRange(AssociationFoundationTests.CreateBusinessGraph(association.Id, user.Id).Cast<object>());
            tenant.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = user.Id, Role = RoleNames.Member });
            await tenant.SaveChangesAsync();
        }

        public async Task<Dictionary<string, string>> SnapshotAsync(bool excludeNewIdentity = false, bool includeMarker = false)
        {
            await using var database = _database.CreateUnresolvedContext();
            await database.Database.OpenConnectionAsync();
            var result = new Dictionary<string, string>();
            var newUserId = excludeNewIdentity ? (await database.PlatformBootstrapStates.SingleAsync()).InitializedUserId : null;
            var newRoleId = excludeNewIdentity ? (await database.Roles.SingleAsync(x => x.Name == RoleNames.PlatformAdministrator)).Id : null;
            foreach (var entity in database.Model.GetEntityTypes().Where(x => includeMarker || x.ClrType != typeof(PlatformBootstrapState)))
            {
                var table = entity.GetTableName()!;
                var columns = string.Join(",", entity.GetProperties().Select(x => $"[{x.GetColumnName()}]"));
                var order = string.Join(",", entity.FindPrimaryKey()!.Properties.Select(x => $"[{x.GetColumnName()}]"));
                var filter = excludeNewIdentity ? table switch
                {
                    "AspNetUsers" => " WHERE [Id] <> @newUserId",
                    "AspNetRoles" => " WHERE [Id] <> @newRoleId",
                    "AspNetUserRoles" or "AspNetUserTokens" => " WHERE [UserId] <> @newUserId",
                    _ => ""
                } : "";
                await using var command = database.Database.GetDbConnection().CreateCommand();
                command.CommandText = $"SELECT {columns} FROM [{table}]{filter} ORDER BY {order} FOR JSON PATH, INCLUDE_NULL_VALUES";
                if (excludeNewIdentity)
                {
                    var userParameter = command.CreateParameter(); userParameter.ParameterName = "@newUserId"; userParameter.Value = newUserId!; command.Parameters.Add(userParameter);
                    var roleParameter = command.CreateParameter(); roleParameter.ParameterName = "@newRoleId"; roleParameter.Value = newRoleId!; command.Parameters.Add(roleParameter);
                }
                await using var reader = await command.ExecuteReaderAsync();
                var json = new StringBuilder();
                while (await reader.ReadAsync()) json.Append(reader.GetString(0));
                result.Add(table, json.ToString());
            }
            return result;
        }

        public async ValueTask DisposeAsync() { await _services.DisposeAsync(); await _database.DisposeAsync(); }
    }
}
