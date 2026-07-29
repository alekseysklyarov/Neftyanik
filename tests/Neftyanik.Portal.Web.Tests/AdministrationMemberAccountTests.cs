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
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Administration.Members;
using Neftyanik.Portal.Web.Security;
using AccountCreateModel = Neftyanik.Portal.Web.Pages.Administration.Members.Account.CreateModel;
using AccountRolesModel = Neftyanik.Portal.Web.Pages.Administration.Members.Account.RolesModel;
using AdministrationMemberEditModel = Neftyanik.Portal.Web.Pages.Administration.Members.EditModel;
using MemberIndexModel = Neftyanik.Portal.Web.Pages.Member.IndexModel;
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
        Assert.False(user.MustChangePassword);

        var member = await dbContext.Members.SingleAsync();
        Assert.Equal(user.Id, member.ApplicationUserId);
    }

    [Fact]
    public async Task OnPostCreateAccountAsync_AllowsDisablingPasswordChangeOnNextLogin()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const int memberId = 2;
        dbContext.Members.Add(new Member
        {
            Id = memberId,
            FullName = "Account Member Two",
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
                Login = "member-login-2",
                TemporaryPassword = "TempPass123!",
                ConfirmPassword = "TempPass123!",
                MustChangePasswordOnLogin = false
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

        var user = await dbContext.Users.SingleAsync(item => item.UserName == "member-login-2");
        Assert.False(user.MustChangePassword);
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

    [Fact]
    public async Task OnPostUpdateProfileAsync_UpdatesMemberAndUserWithoutChangingLogin()
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
            Id = "member-user-1",
            UserName = "member-login",
            NormalizedUserName = "MEMBER-LOGIN",
            Email = "old@example.com",
            NormalizedEmail = "OLD@EXAMPLE.COM",
            FirstName = "Old",
            LastName = "Name",
            DisplayName = "Old Name",
            PhoneNumber = "+380000000000",
            CreatedAt = DateTimeOffset.UtcNow,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true
        };

        dbContext.Users.Add(user);
        dbContext.Members.Add(new Member
        {
            Id = 25,
            FullName = "Старое Имя",
            Email = "old@example.com",
            PhoneNumber = "+380000000000",
            IsActive = true,
            ApplicationUserId = user.Id
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
                }
            }),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var model = new MemberIndexModel(dbContext, userManager)
        {
            Profile = new MemberIndexModel.ProfileInputModel
            {
                FullName = "Новое Полное Имя",
                Email = "new@example.com",
                PhoneNumber = "+380501112233"
            },
            ChargePage = 2,
            PaymentPage = 3,
            PageContext = TestPageModelContext.CreatePageContext(user.Id, user.UserName)
        };

        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);

        var result = await model.OnPostUpdateProfileAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);
        Assert.Equal(2, redirect.RouteValues!["chargePage"]);
        Assert.Equal(3, redirect.RouteValues["paymentPage"]);

        var updatedUser = await dbContext.Users.SingleAsync();
        Assert.Equal("member-login", updatedUser.UserName);
        Assert.Equal("new@example.com", updatedUser.Email);
        Assert.Equal("NEW@EXAMPLE.COM", updatedUser.NormalizedEmail);
        Assert.Equal("Новое Полное", updatedUser.FirstName);
        Assert.Equal("Имя", updatedUser.LastName);
        Assert.Equal("Новое Полное Имя", updatedUser.DisplayName);
        Assert.Equal("+380501112233", updatedUser.PhoneNumber);

        var updatedMember = await dbContext.Members.SingleAsync();
        Assert.Equal("Новое Полное Имя", updatedMember.FullName);
        Assert.Equal("new@example.com", updatedMember.Email);
        Assert.Equal("+380501112233", updatedMember.PhoneNumber);
    }

    [Fact]
    public async Task OnPostChangePasswordAsync_ChangesPasswordForMember()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

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
                    RequireLowercase = false,
                    RequireUppercase = false,
                    RequireNonAlphanumeric = false
                }
            }),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            [new PasswordValidator<ApplicationUser>(), new SimplePasswordValidator()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var user = new ApplicationUser
        {
            Id = "member-user-2",
            UserName = "member-login-2",
            NormalizedUserName = "MEMBER-LOGIN-2",
            CreatedAt = DateTimeOffset.UtcNow,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true,
            MustChangePassword = false
        };

        var createUserResult = await userManager.CreateAsync(user, "abc123");
        Assert.True(createUserResult.Succeeded);

        dbContext.Members.Add(new Member
        {
            Id = 26,
            FullName = "Тестовый Пользователь",
            IsActive = true,
            ApplicationUserId = user.Id
        });
        await dbContext.SaveChangesAsync();

        var model = new MemberIndexModel(dbContext, userManager)
        {
            ChangePassword = new MemberIndexModel.ChangePasswordInputModel
            {
                CurrentPassword = "abc123",
                NewPassword = "def456",
                ConfirmNewPassword = "def456"
            },
            PageContext = TestPageModelContext.CreatePageContext(user.Id, user.UserName)
        };

        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);

        var result = await model.OnPostChangePasswordAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(redirect.PageName);

        var updatedUser = await dbContext.Users.SingleAsync();
        Assert.True(await userManager.CheckPasswordAsync(updatedUser, "def456"));
    }

    [Fact]
    public async Task OnPostEditAsync_UpdatesLinkedAccountLoginAndAllowsEmptyEmail()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        using var userStore = new UserStore<ApplicationUser>(dbContext);
        using var userManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions
            {
                User =
                {
                    RequireUniqueEmail = false
                }
            }),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var user = new ApplicationUser
        {
            Id = "member-user-edit-1",
            UserName = "old-login",
            NormalizedUserName = "OLD-LOGIN",
            Email = "old@example.com",
            NormalizedEmail = "OLD@EXAMPLE.COM",
            FirstName = "Старое",
            LastName = "Имя",
            DisplayName = "Старое Имя",
            PhoneNumber = "+380000000001",
            CreatedAt = DateTimeOffset.UtcNow,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true
        };

        dbContext.Users.Add(user);
        dbContext.Members.Add(new Member
        {
            Id = 28,
            FullName = "Старое Имя",
            Email = "old@example.com",
            PhoneNumber = "+380000000001",
            IsActive = true,
            ApplicationUserId = user.Id
        });
        await dbContext.SaveChangesAsync();

        var model = new AdministrationMemberEditModel(dbContext, userManager)
        {
            Input = new MemberInputModel
            {
                Login = "new-login",
                FullName = "Новое Полное Имя",
                Email = null,
                PhoneNumber = "+380501234567",
                IsActive = true
            },
            PageContext = TestPageModelContext.CreatePageContext()
        };

        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);

        var result = await model.OnPostAsync(28, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Members/Details", redirect.PageName);

        var updatedUser = await dbContext.Users.SingleAsync();
        Assert.Equal("new-login", updatedUser.UserName);
        Assert.Equal("NEW-LOGIN", updatedUser.NormalizedUserName);
        Assert.Null(updatedUser.Email);
        Assert.Null(updatedUser.NormalizedEmail);
        Assert.Equal("Новое Полное", updatedUser.FirstName);
        Assert.Equal("Имя", updatedUser.LastName);
        Assert.Equal("Новое Полное Имя", updatedUser.DisplayName);

        var updatedMember = await dbContext.Members.SingleAsync();
        Assert.Null(updatedMember.Email);
        Assert.Equal("Новое Полное Имя", updatedMember.FullName);
    }

    [Fact]
    public async Task OnPostRolesAsync_AssignsAccountantRoleToExistingMemberAccount()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        using var userStore = new UserStore<ApplicationUser>(dbContext);
        using var roleStore = new RoleStore<IdentityRole>(dbContext);
        using var userManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions
            {
                User =
                {
                    RequireUniqueEmail = false
                }
            }),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);
        using var roleManager = new RoleManager<IdentityRole>(
            roleStore,
            [new RoleValidator<IdentityRole>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole>>.Instance);

        var user = new ApplicationUser
        {
            Id = "member-user-3",
            UserName = "member-login-3",
            NormalizedUserName = "MEMBER-LOGIN-3",
            CreatedAt = DateTimeOffset.UtcNow,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            IsActive = true,
            MustChangePassword = false
        };

        dbContext.Users.Add(user);
        dbContext.Members.Add(new Member
        {
            Id = 27,
            FullName = "Бухгалтер Тестовый",
            IsActive = true,
            ApplicationUserId = user.Id
        });
        await dbContext.SaveChangesAsync();

        var model = new AccountRolesModel(dbContext, userManager, roleManager)
        {
            Input = new AccountRolesModel.InputModel
            {
                IsAccountant = true
            },
            PageContext = TestPageModelContext.CreatePageContext()
        };

        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);

        var result = await model.OnPostAsync(27, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Members/Details", redirect.PageName);
        Assert.True(await userManager.IsInRoleAsync(user, RoleNames.Accountant));
    }

}
#endif
