#if WEB_TESTS
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Administration.Members;
using AccountCreateModel = Neftyanik.Portal.Web.Pages.Administration.Members.Account.CreateModel;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationMemberAccountTests
{
    [Fact]
    public async Task OnPostCreateAccountAsync_AllowsCreatingAccountWithoutEmail()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const int memberId = 1;
        dbContext.Members.Add(new Member
        {
            Id = memberId,
            FullName = "Account Member",
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        using var userStore = new UserStore<ApplicationUser>(dbContext);
        using var userManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions
            {
                User =
                {
                    RequireUniqueEmail = false
                },
                Password =
                {
                    RequiredLength = 6,
                    RequireDigit = true,
                    RequireLowercase = true,
                    RequireUppercase = false,
                    RequireNonAlphanumeric = false
                }
            }),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            [new PasswordValidator<ApplicationUser>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var model = new AccountCreateModel(dbContext, userManager)
        {
            Input = new AccountCreateModel.InputModel
            {
                Login = "member-login",
                TemporaryPassword = "TempPass123!",
                ConfirmPassword = "TempPass123!"
            },
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity())
                }
            }
        };

        var result = await model.OnPostAsync(memberId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Members/Details", redirect.PageName);

        var user = await dbContext.Users.SingleAsync();
        Assert.Equal("member-login", user.UserName);
        Assert.Null(user.Email);

        var member = await dbContext.Members.SingleAsync();
        Assert.Equal(user.Id, member.ApplicationUserId);
    }

    [Fact]
    public async Task OnGetMembersAsync_LoadsLoginIntoMembersTable()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "member-login",
            NormalizedUserName = "MEMBER-LOGIN",
            Email = null,
            FirstName = "Member",
            LastName = "User",
            DisplayName = "Member User",
            CreatedAt = DateTimeOffset.UtcNow,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true
        };

        dbContext.Users.Add(user);
        dbContext.Members.Add(new Member
        {
            Id = 10,
            FullName = "Member With Login",
            IsActive = true,
            ApplicationUserId = user.Id
        });
        await dbContext.SaveChangesAsync();

        var model = new IndexModel(dbContext);
        await model.OnGetAsync(CancellationToken.None);

        var member = Assert.Single(model.Members);
        Assert.Equal("member-login", member.Login);
    }

}
#endif
