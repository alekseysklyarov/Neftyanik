using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Neftyanik.Portal.Application.Exceptions;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Application.Interfaces;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Identity;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class AdminBootstrapServiceTests
{
    [Fact]
    public async Task CreateAdministratorAsync_MissingEmail_ThrowsControlledFailure()
    {
        await using var context = await CreateTestContextAsync();

        var exception = await Assert.ThrowsAsync<AdminBootstrapException>(() => context.Service.CreateAdministratorAsync(CreateRequest(email: null)));

        Assert.Equal(0, context.DbContext.Users.Count());
        Assert.Contains("NEFTYANIK_ADMIN_EMAIL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAdministratorAsync_MissingPassword_ThrowsControlledFailure()
    {
        await using var context = await CreateTestContextAsync();

        var exception = await Assert.ThrowsAsync<AdminBootstrapException>(() => context.Service.CreateAdministratorAsync(CreateRequest(password: null)));

        Assert.Equal(0, context.DbContext.Users.Count());
        Assert.Contains("NEFTYANIK_ADMIN_PASSWORD", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAdministratorAsync_InvalidEmail_ThrowsControlledFailure()
    {
        await using var context = await CreateTestContextAsync();

        var exception = await Assert.ThrowsAsync<AdminBootstrapException>(() => context.Service.CreateAdministratorAsync(CreateRequest(email: "invalid-email")));

        Assert.Equal(0, context.DbContext.Users.Count());
        Assert.Contains("valid email", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAdministratorAsync_ValidRequest_CreatesOneAdministrator()
    {
        var request = CreateRequest();
        await using var context = await CreateTestContextAsync();

        var result = await context.Service.CreateAdministratorAsync(request);

        var user = await context.UserManager.FindByEmailAsync(request.Email!);

        Assert.NotNull(user);
        Assert.True(user!.EmailConfirmed);
        Assert.Equal(1, context.DbContext.Users.Count());
        Assert.True(await context.UserManager.IsInRoleAsync(user, RoleNames.Administrator));
        Assert.Equal(AdminBootstrapOutcome.Created, result.Outcome);
    }

    [Fact]
    public async Task CreateAdministratorAsync_AdministratorAlreadyExists_ReturnsSuccessWithoutDuplicates()
    {
        var request = CreateRequest();
        await using var context = await CreateTestContextAsync();

        await context.Service.CreateAdministratorAsync(request);
        var result = await context.Service.CreateAdministratorAsync(request);

        var user = await context.UserManager.FindByEmailAsync(request.Email!);

        Assert.NotNull(user);
        Assert.Equal(1, context.DbContext.Users.Count());
        Assert.Equal(1, context.DbContext.UserRoles.Count(x => x.UserId == user!.Id));
        Assert.Equal(AdminBootstrapOutcome.AlreadyAdministrator, result.Outcome);
    }

    [Fact]
    public async Task CreateAdministratorAsync_ExistingNonAdminUser_ThrowsControlledFailure()
    {
        var request = CreateRequest();
        await using var context = await CreateTestContextAsync();

        var existingUser = new ApplicationUser
        {
            UserName = request.Email!,
            Email = request.Email!,
            FirstName = "Existing",
            LastName = "User",
            EmailConfirmed = false
        };

        var createResult = await context.UserManager.CreateAsync(existingUser, request.Password!);
        Assert.True(createResult.Succeeded);

        var exception = await Assert.ThrowsAsync<AdminBootstrapException>(() => context.Service.CreateAdministratorAsync(request));

        Assert.False(await context.UserManager.IsInRoleAsync(existingUser, RoleNames.Administrator));
        Assert.Equal(1, context.DbContext.Users.Count());
        Assert.Contains("allow-existing-user-role-assignment", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAdministratorAsync_ExistingNonAdminUser_WithExplicitOption_AssignsAdministratorRole()
    {
        var request = CreateRequest(allowExistingUserRoleAssignment: true);
        await using var context = await CreateTestContextAsync();

        var existingUser = new ApplicationUser
        {
            UserName = request.Email!,
            Email = request.Email!,
            FirstName = "Existing",
            LastName = "User",
            EmailConfirmed = false
        };

        var createResult = await context.UserManager.CreateAsync(existingUser, request.Password!);
        Assert.True(createResult.Succeeded);

        var result = await context.Service.CreateAdministratorAsync(request);

        Assert.True(await context.UserManager.IsInRoleAsync(existingUser, RoleNames.Administrator));
        Assert.Equal(AdminBootstrapOutcome.RoleAssignedToExistingUser, result.Outcome);
    }

    [Fact]
    public async Task CreateAdministratorAsync_InvalidPassword_ThrowsControlledFailure()
    {
        var request = CreateRequest(password: "123");
        await using var context = await CreateTestContextAsync();

        var exception = await Assert.ThrowsAsync<AdminBootstrapException>(() => context.Service.CreateAdministratorAsync(request));

        Assert.Contains("password", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, context.DbContext.Users.Count());
    }

    [Fact]
    public async Task CreateAdministratorAsync_DoesNotWritePasswordToLogs()
    {
        var request = CreateRequest();
        await using var context = await CreateTestContextAsync();

        await context.Service.CreateAdministratorAsync(request);

        Assert.DoesNotContain(context.Logger.Messages, message => message.Contains(request.Password!, StringComparison.Ordinal));
    }

    private static AdminBootstrapRequest CreateRequest(
        string? email = "bootstrap-admin@example.com",
        string? password = "Strong12",
        string? name = "Local Administrator",
        bool allowExistingUserRoleAssignment = false)
    {
        return new AdminBootstrapRequest(email, password, name, allowExistingUserRoleAssignment);
    }

    private static async Task<AdminBootstrapTestContext> CreateTestContextAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequiredLength = 6;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        services.AddScoped<IAdminBootstrapService, AdminBootstrapService>();
        services.AddSingleton<TestLogger<AdminBootstrapService>>();
        services.AddSingleton<ILogger<AdminBootstrapService>>(provider => provider.GetRequiredService<TestLogger<AdminBootstrapService>>());

        var serviceProvider = services.BuildServiceProvider();
        var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var service = scope.ServiceProvider.GetRequiredService<IAdminBootstrapService>();
        var logger = scope.ServiceProvider.GetRequiredService<TestLogger<AdminBootstrapService>>();

        return new AdminBootstrapTestContext(scope, dbContext, userManager, service, logger);
    }

    private sealed class AdminBootstrapTestContext : IAsyncDisposable
    {
        public AdminBootstrapTestContext(
            AsyncServiceScope scope,
            ApplicationDbContext dbContext,
            UserManager<ApplicationUser> userManager,
            IAdminBootstrapService service,
            TestLogger<AdminBootstrapService> logger)
        {
            Scope = scope;
            DbContext = dbContext;
            UserManager = userManager;
            Service = service;
            Logger = logger;
        }

        public AsyncServiceScope Scope { get; }

        public ApplicationDbContext DbContext { get; }

        public UserManager<ApplicationUser> UserManager { get; }

        public IAdminBootstrapService Service { get; }

        public TestLogger<AdminBootstrapService> Logger { get; }

        public ValueTask DisposeAsync()
        {
            return Scope.DisposeAsync();
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
