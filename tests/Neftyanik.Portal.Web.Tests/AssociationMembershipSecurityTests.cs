using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AssociationMembershipSecurityTests
{
    [Theory]
    [InlineData("admin", "neftyanik", "/Administration/Members/Create", true)]
    [InlineData("admin", "second", "/Administration/Members/Create", false)]
    [InlineData("accountant", "neftyanik", "/Administration/Members", true)]
    [InlineData("accountant", "neftyanik", "/Administration/Members/Create", false)]
    [InlineData("accountant", "second", "/Administration/Members/Create", false)]
    [InlineData("member", "neftyanik", "/Member", true)]
    [InlineData("member", "second", "/Member", false)]
    [InlineData("second-admin", "second", "/Administration/Members/Create", true)]
    [InlineData("second-admin", "neftyanik", "/Administration/Members/Create", false)]
    [InlineData("dual", "neftyanik", "/Administration/Members/Create", true)]
    [InlineData("dual", "second", "/Administration/Members/Create", false)]
    [InlineData("dual", "second", "/Member", true)]
    [InlineData("none", "neftyanik", "/Administration/Members/Create", false)]
    [InlineData("none", "neftyanik", "/Account/ChangeInitialPassword", false)]
    [InlineData("inactive", "neftyanik", "/Member", false)]
    public async Task IdentityCookie_GlobalAdministratorClaimNeverOverridesAssociationRoles(string user, string slug, string path, bool allowed)
    {
        await using var fixture = await SecurityFixture.CreateAsync();
        using var client = await fixture.ExistingCookieClientAsync(user);
        using var response = await client.GetAsync($"/{slug}{path}");
        if (allowed)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        else
        {
            AssertDenied(response, slug);
        }
    }

    [Fact]
    public async Task Login_UsesRootCookieAndRevocationIsEffectiveWithoutLogout()
    {
        await using var fixture = await SecurityFixture.CreateAsync();
        var cookies = new CookieContainer();
        using var client = AuthenticationCookieTests.CreateBrowser(fixture.App, cookies);
        using var login = await fixture.LoginAsync(client, "dual", "neftyanik");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Assert.Equal("/neftyanik/Administration", login.Headers.Location?.OriginalString);
        var session = Assert.Single(cookies.GetAllCookies().Cast<Cookie>().Where(x => x.Name == fixture.CookieOptions.Cookie.Name));
        Assert.Equal("/", session.Path);
        Assert.True(session.HttpOnly);
        Assert.True(session.Secure);
        var originalValue = session.Value;
        using var before = await client.GetAsync("/neftyanik/Administration/Members/Create");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        await fixture.ExecuteAsync("neftyanik", async database =>
        {
            var membership = await database.AssociationUserMemberships.SingleAsync(x => x.ApplicationUserId == "dual");
            membership.IsActive = false;
            await database.SaveChangesAsync();
        });
        using var revoked = await client.GetAsync("/neftyanik/Administration/Members/Create");
        AssertDenied(revoked, "neftyanik");
        using var other = await client.GetAsync("/second/Member");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(originalValue, cookies.GetCookies(new Uri("https://localhost"))[fixture.CookieOptions.Cookie.Name!]!.Value);
        await fixture.ExecuteAsync("second", async database =>
        {
            database.AssociationUserMemberships.Remove(await database.AssociationUserMemberships.SingleAsync(x => x.ApplicationUserId == "dual"));
            await database.SaveChangesAsync();
        });
        using var deleted = await client.GetAsync("/second/Member");
        AssertDenied(deleted, "second");
        var token = await AuthenticationCookieTests.TokenAsync(client, "/second/Privacy");
        using var logout = await client.PostAsync("/second/Account/Logout", Form(token));
        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);
        Assert.Equal("/second", logout.Headers.Location?.OriginalString);
        Assert.DoesNotContain(cookies.GetAllCookies().Cast<Cookie>(), x => x.Name == fixture.CookieOptions.Cookie.Name);
    }

    [Theory]
    [InlineData("none", "neftyanik")]
    [InlineData("inactive", "neftyanik")]
    [InlineData("admin", "second")]
    public async Task Login_WithoutActiveMembershipIsGenericAndDoesNotIssueSession(string user, string slug)
    {
        await using var fixture = await SecurityFixture.CreateAsync();
        var cookies = new CookieContainer();
        using var client = AuthenticationCookieTests.CreateBrowser(fixture.App, cookies);
        using var response = await fixture.LoginAsync(client, user, slug);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain(cookies.GetAllCookies().Cast<Cookie>(), x => x.Name == fixture.CookieOptions.Cookie.Name);
        using var unknown = await fixture.LoginAsync(client, "nonexistent", slug);
        Assert.Equal(response.StatusCode, unknown.StatusCode);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Roles")]
    [InlineData("ResetPassword")]
    [InlineData("Lock")]
    public async Task ForeignAccountIds_AreRejectedForGetAndPostWithoutMutatingUsers(string operation)
    {
        await using var fixture = await SecurityFixture.CreateAsync();
        using var client = await fixture.ExistingCookieClientAsync("admin");
        var id = operation == "Create" ? fixture.Unlinked["second"] : fixture.Members[("second", "foreign")];
        var path = $"/neftyanik/Administration/Members/{id}/Account/{operation}";
        using var get = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        var token = await AuthenticationCookieTests.TokenAsync(client, "/neftyanik/Privacy");
        using var post = await client.PostAsync(path, Form(token, ("Input.Login", "forged"), ("Input.IsAccountant", "true"),
            ("Input.TemporaryPassword", "New123!"), ("Input.NewTemporaryPassword", "New123!"), ("Input.ConfirmPassword", "New123!"),
            ("AssociationId", fixture.AssociationIds["second"].ToString())));
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        await fixture.AssertForeignUnchangedAsync();
    }

    [Theory]
    [InlineData("Archive")]
    [InlineData("Edit")]
    [InlineData("Delete")]
    public async Task ForeignMemberMutation_IsRejected(string operation)
    {
        await using var fixture = await SecurityFixture.CreateAsync();
        using var client = await fixture.ExistingCookieClientAsync("admin");
        var token = await AuthenticationCookieTests.TokenAsync(client, "/neftyanik/Privacy");
        using var post = await client.PostAsync($"/neftyanik/Administration/Members/{operation}/{fixture.Members[("second", "foreign")]}",
            Form(token, ("Input.FullName", "Forged"), ("Input.Login", "forged"), ("Input.IsActive", "false")));
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        await fixture.AssertForeignUnchangedAsync();
    }

    [Fact]
    public async Task SharedAccount_ResetAndProfileEditAreDeniedButLocalRoleAndLockDoNotAffectOtherTenant()
    {
        await using var fixture = await SecurityFixture.CreateAsync();
        using var client = await fixture.ExistingCookieClientAsync("admin");
        var token = await AuthenticationCookieTests.TokenAsync(client, "/neftyanik/Privacy");
        var id = fixture.Members[("neftyanik", "dual")];
        using var reset = await client.PostAsync($"/neftyanik/Administration/Members/{id}/Account/ResetPassword", Form(token,
            ("Input.NewTemporaryPassword", "New123!"), ("Input.ConfirmPassword", "New123!")));
        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
        using var edit = await client.PostAsync($"/neftyanik/Administration/Members/Edit/{id}", Form(token,
            ("Input.FullName", "Forged"), ("Input.Login", "forged"), ("Input.IsActive", "true")));
        AssertDenied(edit, "neftyanik");
        using var roles = await client.PostAsync($"/neftyanik/Administration/Members/{id}/Account/Roles", Form(token,
            ("Input.IsAccountant", "true"), ("AssociationId", fixture.AssociationIds["second"].ToString()),
            ("Input.AssociationId", fixture.AssociationIds["second"].ToString())));
        Assert.Equal(HttpStatusCode.Found, roles.StatusCode);
        using var locked = await client.PostAsync($"/neftyanik/Administration/Members/{id}/Account/Lock", Form(token));
        Assert.Equal(HttpStatusCode.Found, locked.StatusCode);
        using var dual = await fixture.ExistingCookieClientAsync("dual");
        using var denied = await dual.GetAsync("/neftyanik/Administration/Members/Create");
        AssertDenied(denied, "neftyanik");
        using var second = await dual.GetAsync("/second/Member");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await fixture.ExecuteAsync("second", async database =>
        {
            var membership = await database.AssociationUserMemberships.SingleAsync(x => x.ApplicationUserId == "dual");
            Assert.Equal(RoleNames.Member, membership.Role);
            Assert.True(membership.IsActive);
            var user = await database.Users.SingleAsync(x => x.Id == "dual");
            Assert.True(user.IsActive);
            Assert.Null(user.LockoutEnd);
            Assert.Equal(fixture.PasswordHashes["dual"], user.PasswordHash);
            Assert.Equal("dual", user.UserName);
        });
    }

    [Fact]
    public async Task AccountCreation_IgnoresForgedAssociationAndGrantsOnlyLocalMemberRole()
    {
        await using var fixture = await SecurityFixture.CreateAsync();
        using var client = await fixture.ExistingCookieClientAsync("admin");
        var token = await AuthenticationCookieTests.TokenAsync(client, "/neftyanik/Privacy");
        using var response = await client.PostAsync($"/neftyanik/Administration/Members/{fixture.Unlinked["neftyanik"]}/Account/Create", Form(token,
            ("Input.Login", "new-local-member"), ("Input.TemporaryPassword", "New123!"), ("Input.ConfirmPassword", "New123!"),
            ("AssociationId", fixture.AssociationIds["second"].ToString()), ("Input.AssociationId", fixture.AssociationIds["second"].ToString())));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        await fixture.ExecuteAsync("neftyanik", async database =>
        {
            var user = await database.Users.SingleAsync(x => x.UserName == "new-local-member");
            var assignment = await database.AssociationUserMemberships.IgnoreQueryFilters().SingleAsync(x => x.ApplicationUserId == user.Id);
            Assert.Equal(fixture.AssociationIds["neftyanik"], assignment.AssociationId);
            Assert.Equal(RoleNames.Member, assignment.Role);
            Assert.Empty(await database.UserRoles.Where(x => x.UserId == user.Id).ToListAsync());
            Assert.Equal(user.Id, (await database.Members.SingleAsync(x => x.Id == fixture.Unlinked["neftyanik"])).ApplicationUserId);
        });
    }

    [Fact]
    public async Task SoleTenantAccount_ResetPreservesIdentityAndArchivingRevokesOnlyLocalAccess()
    {
        await using var fixture = await SecurityFixture.CreateAsync();
        using var client = await fixture.ExistingCookieClientAsync("admin");
        var token = await AuthenticationCookieTests.TokenAsync(client, "/neftyanik/Privacy");
        var id = fixture.Members[("neftyanik", "member")];
        using var response = await client.PostAsync($"/neftyanik/Administration/Members/{id}/Account/ResetPassword", Form(token,
            ("Input.NewTemporaryPassword", "New123!"), ("Input.ConfirmPassword", "New123!")));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        await fixture.ExecuteAsync("neftyanik", async database =>
        {
            Assert.NotEqual(fixture.PasswordHashes["member"], (await database.Users.SingleAsync(x => x.Id == "member")).PasswordHash);
            Assert.Equal("member", (await database.Members.SingleAsync(x => x.Id == id)).ApplicationUserId);
        });
        using var archived = await client.PostAsync($"/neftyanik/Administration/Members/Archive/{id}", Form(token));
        Assert.Equal(HttpStatusCode.Found, archived.StatusCode);
        using var member = await fixture.ExistingCookieClientAsync("member");
        using var denied = await member.GetAsync("/neftyanik/Member");
        AssertDenied(denied, "neftyanik");
        await fixture.AssertForeignUnchangedAsync();
    }

    [Fact]
    public async Task UserActivity_ExcludesForeignUsersAndOtherAssociationsLoginEvents()
    {
        await using var fixture = await SecurityFixture.CreateAsync();
        using var client = AuthenticationCookieTests.CreateBrowser(fixture.App, new CookieContainer());
        using var login = await fixture.LoginAsync(client, "dual", "second");
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        foreach (var slug in new[] { "neftyanik", "second" })
        {
            await using var scope = fixture.App.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(await database.Associations.SingleAsync(x => x.Slug == slug));
            var activity = await scope.ServiceProvider.GetRequiredService<IUserActivityService>().GetUserActivityAsync();
            Assert.Equal(slug == "second" ? 1 : 0, activity.Single(x => x.UserId == "dual").TotalSuccessfulLogins);
            Assert.DoesNotContain(activity, x => x.UserId == (slug == "second" ? "admin" : "foreign"));
        }
    }

    [Theory]
    [InlineData("neftyanik")]
    [InlineData("second")]
    public async Task PublicPages_AreAnonymousAndPrivatePagesChallengeWithinTenant(string slug)
    {
        await using var fixture = await SecurityFixture.CreateAsync();
        using var client = AuthenticationCookieTests.CreateBrowser(fixture.App, new CookieContainer());
        foreach (var path in new[] { "/", "/Account/Login", "/Privacy", "/Account/AccessDenied", "/css/site.css" })
        {
            using var response = await client.GetAsync($"/{slug}{path}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var protectedPage = await client.GetAsync($"/{slug}/Member");
        Assert.Equal(HttpStatusCode.Found, protectedPage.StatusCode);
        Assert.StartsWith($"https://localhost/{slug}/Account/Login", protectedPage.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("member", false)]
    [InlineData("dual", true)]
    public async Task LegacyGlobalLockout_CanOnlyBeClearedForExclusiveAssociationAccount(string userId, bool shared)
    {
        await using var fixture = await SecurityFixture.CreateAsync();
        var lockoutEnd = DateTimeOffset.UtcNow.AddYears(1);
        await fixture.ExecuteAsync("neftyanik", async database =>
        {
            var user = await database.Users.SingleAsync(x => x.Id == userId);
            user.LockoutEnabled = true;
            user.LockoutEnd = lockoutEnd;
            await database.SaveChangesAsync();
        });
        using var client = await fixture.ExistingCookieClientAsync("admin");
        var token = await AuthenticationCookieTests.TokenAsync(client, "/neftyanik/Privacy");
        using var response = await client.PostAsync($"/neftyanik/Administration/Members/{fixture.Members[("neftyanik", userId)]}/Account/Lock", Form(token));
        if (shared) AssertDenied(response, "neftyanik");
        else
        {
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Contains("/Administration/Members/Details/", response.Headers.Location!.OriginalString);
        }
        await fixture.ExecuteAsync("neftyanik", async database =>
        {
            var user = await database.Users.SingleAsync(x => x.Id == userId);
            Assert.Equal(shared ? lockoutEnd : (DateTimeOffset?)null, user.LockoutEnd);
            Assert.True(user.LockoutEnabled);
            Assert.True((await database.AssociationUserMemberships.SingleAsync(x => x.ApplicationUserId == userId)).IsActive);
        });
        if (shared)
        {
            await fixture.ExecuteAsync("second", async database =>
                Assert.True((await database.AssociationUserMemberships.SingleAsync(x => x.ApplicationUserId == userId)).IsActive));
        }
        else
        {
            using var browser = AuthenticationCookieTests.CreateBrowser(fixture.App, new CookieContainer());
            using var login = await fixture.LoginAsync(browser, userId, "neftyanik");
            Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AccountDetails_DistinguishesAssociationDisablingFromGlobalLockout(bool locallyDisabled, bool globallyLocked)
    {
        await using var fixture = await SecurityFixture.CreateAsync();
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(await database.Associations.SingleAsync(x => x.Slug == "neftyanik"));
        var user = await database.Users.SingleAsync(x => x.Id == "member");
        user.LockoutEnabled = true;
        user.LockoutEnd = globallyLocked ? DateTimeOffset.UtcNow.AddYears(1) : null;
        (await database.AssociationUserMemberships.SingleAsync(x => x.ApplicationUserId == "member")).IsActive = !locallyDisabled;
        await database.SaveChangesAsync();
        var model = new Neftyanik.Portal.Web.Pages.Administration.Members.DetailsModel(database, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>())
        {
            PageContext = TestPageModelContext.CreatePageContext()
        };
        Assert.IsType<Microsoft.AspNetCore.Mvc.RazorPages.PageResult>(await model.OnGetAsync(fixture.Members[("neftyanik", "member")], CancellationToken.None));
        Assert.Equal(locallyDisabled, model.Member.Account.IsAssociationAccessDisabled);
        Assert.Equal(globallyLocked, model.Member.Account.IsGloballyLockedOut);
        Assert.Equal(locallyDisabled || globallyLocked, model.Member.Account.IsLockedOut);
        Assert.Equal(locallyDisabled ? 0 : 1, model.Member.Account.Roles.Count);
        Assert.Equal(user.LockoutEnd, model.Member.Account.LockoutEnd);
        Assert.NotEmpty(model.Member.Account.StatusText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExclusiveAccount_ProfileEditPreservesLegacyActivationBehavior(bool activate)
    {
        await using var fixture = await SecurityFixture.CreateAsync();
        var id = fixture.Members[("neftyanik", "member")];
        await fixture.ExecuteAsync("neftyanik", async database =>
        {
            (await database.Users.SingleAsync(x => x.Id == "member")).IsActive = !activate;
            (await database.Members.SingleAsync(x => x.Id == id)).IsActive = !activate;
            (await database.AssociationUserMemberships.SingleAsync(x => x.ApplicationUserId == "member")).IsActive = !activate;
            await database.SaveChangesAsync();
        });
        using var client = await fixture.ExistingCookieClientAsync("admin");
        var token = await AuthenticationCookieTests.TokenAsync(client, "/neftyanik/Privacy");
        using var response = await client.PostAsync($"/neftyanik/Administration/Members/Edit/{id}", Form(token,
            ("Input.Login", "member"), ("Input.FullName", "Member Name"), ("Input.IsActive", activate.ToString())));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/Administration/Members/Details/", response.Headers.Location!.OriginalString);
        await fixture.ExecuteAsync("neftyanik", async database =>
        {
            Assert.Equal(activate, (await database.Users.SingleAsync(x => x.Id == "member")).IsActive);
            Assert.Equal(activate, (await database.AssociationUserMemberships.SingleAsync(x => x.ApplicationUserId == "member")).IsActive);
            Assert.Equal(fixture.PasswordHashes["member"], (await database.Users.SingleAsync(x => x.Id == "member")).PasswordHash);
        });
        await fixture.AssertForeignUnchangedAsync();
    }

    [Fact]
    public async Task AccountCreation_ExistingGlobalUsernameDoesNotLinkOrGrantPermissions()
    {
        await using var fixture = await SecurityFixture.CreateAsync();
        using var client = await fixture.ExistingCookieClientAsync("second-admin");
        var token = await AuthenticationCookieTests.TokenAsync(client, "/second/Privacy");
        using var response = await client.PostAsync($"/second/Administration/Members/{fixture.Unlinked["second"]}/Account/Create", Form(token,
            ("Input.Login", "member"), ("Input.TemporaryPassword", "New123!"), ("Input.ConfirmPassword", "New123!")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await fixture.ExecuteAsync("second", async database =>
        {
            Assert.False(await database.AssociationUserMemberships.AnyAsync(x => x.ApplicationUserId == "member"));
            Assert.Null((await database.Members.SingleAsync(x => x.Id == fixture.Unlinked["second"])).ApplicationUserId);
            Assert.Equal(fixture.PasswordHashes["member"], (await database.Users.SingleAsync(x => x.Id == "member")).PasswordHash);
        });
    }

    private static void AssertDenied(HttpResponseMessage response, string slug)
    {
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith($"https://localhost/{slug}/Account/AccessDenied", response.Headers.Location!.OriginalString);
    }

    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] values) =>
        new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)).Prepend(new("__RequestVerificationToken", token)));

    private sealed class SecurityFixture : IAsyncDisposable
    {
        private readonly PortalWebApplicationFactory _factory = new();
        public WebApplicationFactory<Program> App { get; private set; } = null!;
        public Dictionary<string, int> AssociationIds { get; } = new();
        public Dictionary<(string Slug, string User), int> Members { get; } = new();
        public Dictionary<string, int> Unlinked { get; } = new();
        public Dictionary<string, string?> PasswordHashes { get; } = new();
        public CookieAuthenticationOptions CookieOptions => App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);

        public static async Task<SecurityFixture> CreateAsync()
        {
            var fixture = new SecurityFixture();
            fixture.App = AuthenticationCookieTests.CreateCookieApplication(fixture._factory);
            await using (var scope = fixture.App.Services.CreateAsyncScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                database.Associations.Add(new Association { Slug = "second", Name = "Second" });
                await database.SaveChangesAsync();
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                foreach (var id in new[] { "admin", "accountant", "member", "second-admin", "dual", "none", "inactive", "foreign" })
                {
                    var user = new ApplicationUser { Id = id, UserName = id, FirstName = id, LastName = "Test", IsActive = true };
                    Assert.True((await manager.CreateAsync(user, "Cookie123!")).Succeeded);
                    Assert.True((await manager.AddToRoleAsync(user, RoleNames.Administrator)).Succeeded);
                    fixture.PasswordHashes[id] = user.PasswordHash;
                }
            }
            foreach (var slug in new[] { "neftyanik", "second" })
            {
                await fixture.ExecuteAsync(slug, async database =>
                {
                    fixture.AssociationIds[slug] = database.CurrentAssociationId;
                    var assignments = slug == "neftyanik"
                        ? new[] { ("admin", RoleNames.Administrator), ("accountant", RoleNames.Accountant), ("member", RoleNames.Member), ("dual", RoleNames.Administrator), ("inactive", RoleNames.Member) }
                        : new[] { ("second-admin", RoleNames.Administrator), ("dual", RoleNames.Member), ("foreign", RoleNames.Member) };
                    database.AssociationUserMemberships.AddRange(assignments.Select(x => new AssociationUserMembership { ApplicationUserId = x.Item1, Role = x.Item2, IsActive = x.Item1 != "inactive" }));
                    foreach (var id in slug == "neftyanik" ? new[] { "member", "dual" } : new[] { "foreign", "dual" })
                    {
                        var member = new Member { FullName = $"member-{slug}-{id}", ApplicationUserId = id };
                        database.Members.Add(member);
                        await database.SaveChangesAsync();
                        fixture.Members[(slug, id)] = member.Id;
                    }
                    var unlinked = new Member { FullName = $"unlinked-{slug}" };
                    database.Members.Add(unlinked);
                    await database.SaveChangesAsync();
                    fixture.Unlinked[slug] = unlinked.Id;
                });
            }
            return fixture;
        }

        public async Task ExecuteAsync(string slug, Func<ApplicationDbContext, Task> action)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(await database.Associations.SingleAsync(x => x.Slug == slug));
            await action(database);
        }

        public async Task<HttpClient> ExistingCookieClientAsync(string userId)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
            var user = await manager.UserManager.FindByIdAsync(userId);
            var principal = await manager.CreateUserPrincipalAsync(user!);
            Assert.True(principal.IsInRole(RoleNames.Administrator));
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("dachahub:association-role", RoleNames.Administrator));
            var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
            }, IdentityConstants.ApplicationScheme);
            var cookies = new CookieContainer();
            cookies.Add(new Uri("https://localhost"), new Cookie(CookieOptions.Cookie.Name!, CookieOptions.TicketDataFormat.Protect(ticket), "/"));
            return AuthenticationCookieTests.CreateBrowser(App, cookies);
        }

        public async Task<HttpResponseMessage> LoginAsync(HttpClient client, string user, string slug)
        {
            var token = await AuthenticationCookieTests.TokenAsync(client, $"/{slug}/Account/Login");
            return await client.PostAsync($"/{slug}/Account/Login", Form(token, ("Input.Login", user), ("Input.Password", "Cookie123!")));
        }

        public Task AssertForeignUnchangedAsync() => ExecuteAsync("second", async database =>
        {
            var user = await database.Users.SingleAsync(x => x.Id == "foreign");
            Assert.Equal(PasswordHashes["foreign"], user.PasswordHash);
            Assert.True(user.IsActive);
            Assert.Null(user.LockoutEnd);
            Assert.Equal("foreign", user.UserName);
            var membership = await database.AssociationUserMemberships.SingleAsync(x => x.ApplicationUserId == "foreign");
            Assert.Equal(RoleNames.Member, membership.Role);
            Assert.True(membership.IsActive);
            Assert.True((await database.Members.SingleAsync(x => x.ApplicationUserId == "foreign")).IsActive);
            Assert.Null((await database.Members.SingleAsync(x => x.Id == Unlinked["second"])).ApplicationUserId);
            Assert.False(await database.Users.AnyAsync(x => x.UserName == "forged"));
        });

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
