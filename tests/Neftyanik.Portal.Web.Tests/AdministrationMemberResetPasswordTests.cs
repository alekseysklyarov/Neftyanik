using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Administration.Members.Account;
using Neftyanik.Portal.Web.Security;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationMemberResetPasswordTests
{
    [Fact]
    public async Task OnPostAsync_DoesNotRequirePasswordChangeWhenCheckboxIsNotSelected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        using var userStore = new UserStore<ApplicationUser>(dbContext);
        using var userManager = CreateUserManager(userStore);

        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "member-login",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            MustChangePassword = true
        };

        var createResult = await userManager.CreateAsync(user, "abc123");
        Assert.True(createResult.Succeeded);

        dbContext.Members.Add(new Member
        {
            Id = 1,
            FullName = "Member One",
            IsActive = true,
            ApplicationUserId = user.Id
        });
        await dbContext.SaveChangesAsync();

        var model = new ResetPasswordModel(dbContext, userManager)
        {
            Input = new ResetPasswordModel.InputModel
            {
                NewTemporaryPassword = "def456",
                ConfirmPassword = "def456",
                MustChangePasswordOnLogin = false
            },
            PageContext = TestPageModelContext.CreatePageContext()
        };

        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);

        var result = await model.OnPostAsync(1, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);

        var updatedUser = await dbContext.Users.SingleAsync();
        Assert.False(updatedUser.MustChangePassword);
    }

    private static UserManager<ApplicationUser> CreateUserManager(UserStore<ApplicationUser> userStore)
    {
        var identityOptions = new IdentityOptions
        {
            User =
            {
                RequireUniqueEmail = false
            },
            Password =
            {
                RequiredLength = 6,
                RequireDigit = true,
                RequireLowercase = false,
                RequireUppercase = false,
                RequireNonAlphanumeric = false
            }
        };
        identityOptions.Tokens.ProviderMap[TokenOptions.DefaultProvider] = new TokenProviderDescriptor(typeof(TestTokenProvider));

        var services = new ServiceCollection();
        services.AddSingleton<TestTokenProvider>();

        return new UserManager<ApplicationUser>(
            userStore,
            Options.Create(identityOptions),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            [new PasswordValidator<ApplicationUser>(), new SimplePasswordValidator()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services.BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private sealed class TestTokenProvider : IUserTwoFactorTokenProvider<ApplicationUser>
    {
        public Task<string> GenerateAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user)
        {
            return Task.FromResult("test-token");
        }

        public Task<bool> ValidateAsync(string purpose, string token, UserManager<ApplicationUser> manager, ApplicationUser user)
        {
            return Task.FromResult(token == "test-token");
        }

        public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
        {
            return Task.FromResult(false);
        }
    }
}
