using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Identity;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class PlatformAdministratorOnboardingTests
{
    [Fact]
    public async Task Bootstrap_CreatesDedicatedPendingIdentity_WithoutTenantData()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(PlatformBootstrapResult.Created, await fixture.BootstrapAsync("first", Secret()));
        await fixture.WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync();
            Assert.True(user.MustChangePassword);
            Assert.True(user.IsActive);
            Assert.True(user.LockoutEnabled);
            Assert.False(user.TwoFactorEnabled);
            Assert.True(await services.GetRequiredService<UserManager<ApplicationUser>>().IsInRoleAsync(user, RoleNames.PlatformAdministrator));
            Assert.Single(await db.UserRoles.ToListAsync());
            Assert.Single(await db.PlatformBootstrapStates.ToListAsync());
            Assert.Equal("first@example.test", user.Email);
            Assert.False(user.EmailConfirmed);
            Assert.Empty(await db.AssociationUserMemberships.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.Members.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await db.Payments.IgnoreQueryFilters().ToListAsync());
        });
    }

    [Fact]
    public async Task Bootstrap_ConcurrentCommands_CreateExactlyOneAdministrator()
    {
        await using var fixture = await Fixture.CreateAsync();
        var results = await Task.WhenAll(fixture.BootstrapAsync("first-a", Secret()), fixture.BootstrapAsync("first-b", Secret()));
        Assert.Single(results.Where(x => x == PlatformBootstrapResult.Created));
        Assert.Single(results.Where(x => x == PlatformBootstrapResult.AlreadyProvisioned));
        await fixture.WithScopeAsync(async services => Assert.Equal(1, await services.GetRequiredService<ApplicationDbContext>().Users.CountAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bootstrap_ConsumedMarkerSurvivesRoleRevocationOrAccountDeletion(bool deleteAccount)
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(PlatformBootstrapResult.Created, await fixture.BootstrapAsync("first", Secret()));
        await fixture.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync("first"))!;
            Assert.True((deleteAccount ? await users.DeleteAsync(user) : await users.RemoveFromRoleAsync(user, RoleNames.PlatformAdministrator)).Succeeded);
        });
        Assert.Equal(PlatformBootstrapResult.AlreadyProvisioned, await fixture.BootstrapAsync("replacement", Secret()));
    }

    [Fact]
    public async Task Bootstrap_RoleDeletedAndRecreated_RemainsPermanentlyClosed()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(PlatformBootstrapResult.Created, await fixture.BootstrapAsync("first", Secret()));
        await fixture.WithScopeAsync(async services =>
        {
            var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.DeleteAsync((await roles.FindByNameAsync(RoleNames.PlatformAdministrator))!)).Succeeded);
            Assert.True((await roles.CreateAsync(new IdentityRole(RoleNames.PlatformAdministrator))).Succeeded);
        });
        Assert.Equal(PlatformBootstrapResult.AlreadyProvisioned, await fixture.BootstrapAsync("replacement", Secret()));
        await fixture.WithScopeAsync(async services => Assert.Single(await services.GetRequiredService<ApplicationDbContext>().PlatformBootstrapStates.ToListAsync()));
    }

    [Fact]
    public async Task Bootstrap_ExistingTenantAccountIsNeverModifiedOrPromoted()
    {
        await using var fixture = await Fixture.CreateAsync();
        var originalPassword = Secret();
        string id = string.Empty;
        await fixture.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = "existing", FirstName = "Existing", LastName = "Tenant" };
            Assert.True((await users.CreateAsync(user, originalPassword)).Succeeded);
            id = user.Id;
        });
        await using (var tenant = fixture.Database.CreateContext())
        {
            tenant.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = id, Role = RoleNames.Administrator });
            await tenant.SaveChangesAsync();
        }
        Assert.Equal(PlatformBootstrapResult.AccountExists, await fixture.BootstrapAsync("EXISTING", Secret()));
        await fixture.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(id))!;
            Assert.True(await users.CheckPasswordAsync(user, originalPassword));
            Assert.False(await users.IsInRoleAsync(user, RoleNames.PlatformAdministrator));
            Assert.False(user.MustChangePassword);
        });
    }

    [Fact]
    public async Task Bootstrap_MissingMarkerInNonPristineStore_FailsClosed()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.CreateAsync(new ApplicationUser { UserName = "tenant", FirstName = "Tenant", LastName = "User" }, Secret())).Succeeded);
        });
        Assert.Equal(PlatformBootstrapResult.AlreadyProvisioned, await fixture.BootstrapAsync("new-operator", Secret()));
        await fixture.WithScopeAsync(async services =>
        {
            var database = services.GetRequiredService<ApplicationDbContext>();
            Assert.Single(await database.Users.ToListAsync());
            Assert.Equal(Neftyanik.Portal.Domain.Enums.PlatformBootstrapDisposition.Consumed,
                (await database.PlatformBootstrapStates.SingleAsync()).Disposition);
            Assert.False(await database.Roles.AnyAsync(x => x.Name == RoleNames.PlatformAdministrator));
        });
    }

    [Fact]
    public async Task Bootstrap_ExistingPlatformAssignmentWithoutMarkerPreventsBootstrap()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.WithScopeAsync(async services =>
        {
            var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.CreateAsync(new IdentityRole(RoleNames.PlatformAdministrator))).Succeeded);
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = "prior", FirstName = "Prior", LastName = "Operator", IsActive = false };
            Assert.True((await users.CreateAsync(user, Secret())).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, RoleNames.PlatformAdministrator)).Succeeded);
        });
        Assert.Equal(PlatformBootstrapResult.AlreadyProvisioned, await fixture.BootstrapAsync("another", Secret()));
    }

    [Fact]
    public async Task Bootstrap_MarkerWriteFailureRollsBackIdentityRoleAndAssignment()
    {
        await using var fixture = await Fixture.CreateAsync(new FailWritesInterceptor("INSERT INTO [PlatformBootstrapStates]"));
        Assert.Equal(PlatformBootstrapResult.Failed, await fixture.BootstrapAsync("first", Secret()));
        await fixture.WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.Users.ToListAsync());
            Assert.Empty(await db.UserRoles.ToListAsync());
            Assert.False(await db.Roles.AnyAsync(x => x.Name == RoleNames.PlatformAdministrator));
        });
    }

    [Fact]
    public async Task ChangePassword_IsAtomic_WhenExpiryRemovalFails()
    {
        var interceptor = new FailWritesInterceptor("DELETE FROM [AspNetUserTokens]");
        await using var fixture = await Fixture.CreateAsync(interceptor);
        var temporary = Secret();
        Assert.Equal(PlatformBootstrapResult.Created, await fixture.BootstrapAsync("first", temporary));
        await fixture.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync("first"))!;
            var result = await services.GetRequiredService<IPlatformAdministratorOnboarding>().ChangePasswordAsync(user.Id, user.SecurityStamp!, temporary, Secret());
            Assert.Equal(PlatformPasswordChangeResult.Failed, result);
        });
        await fixture.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync("first"))!;
            Assert.True(user.MustChangePassword);
            Assert.True(await users.CheckPasswordAsync(user, temporary));
            Assert.NotNull(await users.GetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider, PlatformAdministratorOnboarding.ExpiryToken));
        });
    }

    [Fact]
    public async Task Bootstrap_InvalidPasswordLeavesNoPrivilegedIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(PlatformBootstrapResult.InvalidInput, await fixture.BootstrapAsync("first", new string('x', 1)));
        await fixture.WithScopeAsync(async services => Assert.Empty(await services.GetRequiredService<ApplicationDbContext>().Users.ToListAsync()));
    }

    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA1!";

    private sealed class FailWritesInterceptor(string fragment) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(fragment, StringComparison.Ordinal)) throw new InvalidOperationException("Injected persistence failure.");
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public AssociationDatabaseFixture Database { get; } = new();
        private ServiceProvider _services = null!;

        public static async Task<Fixture> CreateAsync(DbCommandInterceptor? interceptor = null)
        {
            var fixture = new Fixture();
            await fixture.Database.InitializeAsync();
            await using var db = fixture.Database.CreateUnresolvedContext();
            var connection = db.Database.GetConnectionString();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlServer(connection);
                if (interceptor is not null) options.AddInterceptors(interceptor);
            });
            services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
            services.AddScoped<IPlatformAdministratorOnboarding, PlatformAdministratorOnboarding>();
            fixture._services = services.BuildServiceProvider();
            return fixture;
        }

        public async Task<PlatformBootstrapResult> BootstrapAsync(string login, string password)
        {
            await using var scope = _services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IPlatformAdministratorOnboarding>().BootstrapAsync(login, login + "@example.test", password);
        }

        public async Task WithScopeAsync(Func<IServiceProvider, Task> action)
        {
            await using var scope = _services.CreateAsyncScope();
            await action(scope.ServiceProvider);
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            await Database.DisposeAsync();
        }
    }
}
