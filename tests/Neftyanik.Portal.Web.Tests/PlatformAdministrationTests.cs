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
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Identity;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class PlatformAdministrationTests
{
    private const string LoginPath = "/Platform/Account/Login";
    private const string Password = "PlatformTest123!";

    [Theory]
    [InlineData("/Platform")]
    [InlineData("/Platform/")]
    [InlineData("/Platform/Index")]
    [InlineData("/platform/")]
    [InlineData("/PLATFORM/")]
    public async Task AnonymousPlatformPage_ChallengesAtPlatformLogin(string path)
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(LoginPath, response.Headers.Location?.OriginalString);
        using var login = await client.GetAsync(LoginPath);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("__RequestVerificationToken", await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ApplicationStartup_DoesNotSeedPlatformRoleOrUsers()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async database =>
        {
            Assert.False(await database.Roles.AnyAsync(x => x.Name == RoleNames.PlatformAdministrator));
            Assert.Empty(await database.Users.ToListAsync());
            Assert.Empty(await database.AssociationUserMemberships.ToListAsync());
        });
    }

    [Theory]
    [InlineData("ordinary", false)]
    [InlineData("tenant-admin", false)]
    [InlineData("global-admin", false)]
    [InlineData("claim-only", false)]
    [InlineData("operator", true)]
    [InlineData("inactive", false)]
    [InlineData("locked", false)]
    [InlineData("change-password", false)]
    public async Task PlatformPolicy_UsesCurrentIdentityStoreNotCookieRoleClaims(string kind, bool allowed)
    {
        await using var fixture = await Fixture.CreateAsync(kind);
        using var client = await fixture.ExistingSessionAsync(forgePlatformClaim: true);
        using var response = await client.GetAsync("/Platform/");
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlatformLogin_WithoutMembership_UsesIdentityCookieAndSupportsCsrfProtectedLogout()
    {
        await using var fixture = await Fixture.CreateAsync("operator");
        var cookies = new CookieContainer();
        using var client = fixture.Browser(cookies);
        using var response = await fixture.LoginAsync(client);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Platform", response.Headers.Location?.OriginalString?.TrimEnd('/'));
        var cookie = Assert.Single(cookies.GetAllCookies().Cast<Cookie>().Where(x => x.Name == fixture.CookieOptions.Cookie.Name));
        Assert.Equal("/", cookie.Path);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        using var landing = await client.GetAsync("/Platform/");
        Assert.Equal(HttpStatusCode.OK, landing.StatusCode);
        Assert.Contains("Platform access verified", await landing.ReadDecodedHtmlAsync());
        await fixture.WithDatabaseAsync(async database =>
        {
            Assert.Empty(await database.AssociationUserMemberships.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await database.Members.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await database.Payments.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await database.Charges.IgnoreQueryFilters().ToListAsync());
        });
        using var noToken = await client.PostAsync("/Platform/?handler=Logout", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        var token = await AuthenticationCookieTests.TokenAsync(client, "/Platform/");
        using var logout = await client.PostAsync("/Platform/?handler=Logout", Form(token));
        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);
        Assert.Equal(LoginPath, logout.Headers.Location?.OriginalString);
        using var after = await client.GetAsync("/Platform/");
        Assert.Equal(HttpStatusCode.Found, after.StatusCode);
        Assert.DoesNotContain(cookies.GetAllCookies().Cast<Cookie>(), x => x.Name == fixture.CookieOptions.Cookie.Name);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("tenant-admin")]
    [InlineData("global-admin")]
    [InlineData("inactive")]
    [InlineData("locked")]
    [InlineData("change-password")]
    [InlineData("two-factor")]
    [InlineData("operator-wrong-password")]
    public async Task PlatformLogin_InvalidOrUnauthorizedCredentials_NeverIssueApplicationCookie(string kind)
    {
        await using var fixture = await Fixture.CreateAsync(kind);
        var cookies = new CookieContainer();
        using var client = fixture.Browser(cookies);
        using var response = await fixture.LoginAsync(client, password: kind == "operator-wrong-password" ? "Wrong123!" : Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Platform sign-in is unavailable", await response.ReadDecodedHtmlAsync());
        Assert.DoesNotContain(cookies.GetAllCookies().Cast<Cookie>(), x => x.Name == fixture.CookieOptions.Cookie.Name);
        using var landing = await client.GetAsync("/Platform/");
        Assert.Equal(HttpStatusCode.Found, landing.StatusCode);
    }

    [Fact]
    public async Task PlatformLogin_RejectsMissingAndInvalidAntiforgeryTokens()
    {
        await using var fixture = await Fixture.CreateAsync("operator");
        var cookies = new CookieContainer();
        using var client = fixture.Browser(cookies);
        foreach (var token in new[] { "", "forged-token" })
        {
            using var response = await client.PostAsync(LoginPath, Form(token, ("Input.Login", fixture.UserId), ("Input.Password", Password)));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.DoesNotContain(cookies.GetAllCookies().Cast<Cookie>(), x => x.Name == fixture.CookieOptions.Cookie.Name);
    }

    [Theory]
    [InlineData("https://example.invalid/")]
    [InlineData("//example.invalid/")]
    [InlineData("/neftyanik/Administration")]
    [InlineData("/Platform/../demo/Administration")]
    public async Task PlatformLogin_DoesNotFollowExternalOrTenantReturnUrl(string returnUrl)
    {
        await using var fixture = await Fixture.CreateAsync("operator");
        using var client = fixture.Browser(new CookieContainer());
        using var response = await fixture.LoginAsync(client, returnUrl: returnUrl);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Platform", response.Headers.Location?.OriginalString?.TrimEnd('/'));
    }

    [Theory]
    [InlineData("role")]
    [InlineData("inactive")]
    [InlineData("locked")]
    [InlineData("change-password")]
    public async Task PlatformAccess_RevocationTakesEffectWithExistingCookie(string reason)
    {
        await using var fixture = await Fixture.CreateAsync("operator");
        using var client = fixture.Browser(new CookieContainer());
        using var login = await fixture.LoginAsync(client);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        await fixture.WithDatabaseAsync(async database =>
        {
            var user = await database.Users.SingleAsync(x => x.Id == fixture.UserId);
            if (reason == "role") database.UserRoles.RemoveRange(await database.UserRoles.Where(x => x.UserId == user.Id).ToListAsync());
            if (reason == "inactive") user.IsActive = false;
            if (reason == "locked") user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            if (reason == "change-password") user.MustChangePassword = true;
            await database.SaveChangesAsync();
        });
        using var response = await client.GetAsync("/Platform/");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlatformRole_WithoutTenantMembership_CannotAccessEitherTenantsProtectedPagesOrSignIn()
    {
        await using var fixture = await Fixture.CreateAsync("operator");
        using var client = fixture.Browser(new CookieContainer());
        using var login = await fixture.LoginAsync(client);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        foreach (var slug in new[] { "neftyanik", "demo" })
        {
            foreach (var path in new[] { "/Member", "/Administration", "/Administration/Finance", "/Payments/1/Receipt" })
            {
                using var response = await client.GetAsync($"/{slug}{path}");
                Assert.Equal(HttpStatusCode.Found, response.StatusCode);
                Assert.Contains($"/{slug}/Account/AccessDenied", response.Headers.Location!.OriginalString);
            }
            using var tenantClient = fixture.Browser(new CookieContainer());
            var token = await AuthenticationCookieTests.TokenAsync(tenantClient, $"/{slug}/Account/Login");
            using var attempt = await tenantClient.PostAsync($"/{slug}/Account/Login", Form(token, ("Input.Login", fixture.UserId), ("Input.Password", Password)));
            Assert.Equal(HttpStatusCode.OK, attempt.StatusCode);
            using var denied = await tenantClient.GetAsync($"/{slug}/Administration");
            Assert.Contains($"/{slug}/Account/Login", denied.Headers.Location!.OriginalString);
        }
    }

    [Fact]
    public async Task TenantAdministrator_LoginAndLocalAccessRemainTenantScoped()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-admin");
        using var client = fixture.Browser(new CookieContainer());
        var token = await AuthenticationCookieTests.TokenAsync(client, "/neftyanik/Account/Login");
        using var login = await client.PostAsync("/neftyanik/Account/Login", Form(token, ("Input.Login", fixture.UserId), ("Input.Password", Password)));
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        using var own = await client.GetAsync("/neftyanik/Administration");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        using var platform = await client.GetAsync("/Platform/");
        Assert.Equal(HttpStatusCode.Forbidden, platform.StatusCode);
        using var other = await client.GetAsync("/demo/Administration");
        Assert.Contains("/demo/Account/AccessDenied", other.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/neftyanik/Platform/")]
    [InlineData("/demo/Platform/Account/Login")]
    [InlineData("/demo/platform/Index")]
    [InlineData("/PlatformOther/")]
    public async Task Routing_RejectsTenantPrefixedPlatformPagesAndPrefixLookalikes(string path)
    {
        await using var fixture = await Fixture.CreateAsync("operator");
        using var client = await fixture.ExistingSessionAsync();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Routing_PreservesRootRedirectAndReservesPlatformSlug()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("/neftyanik/", response.Headers.Location?.OriginalString);
        Assert.Throws<InvalidOperationException>(() => new Association { Slug = "platform", Name = "Forbidden" }.ValidateSlug());
    }

    [Fact]
    public async Task TenantAdministrator_CannotResetPlatformAccountEvenWithSoleLocalMembership()
    {
        await using var fixture = await Fixture.CreateAsync("tenant-admin");
        var memberId = 0;
        string? originalPasswordHash = null;
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(await database.Associations.SingleAsync(x => x.Slug == "neftyanik"));
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.CreateAsync(new IdentityRole(RoleNames.PlatformAdministrator))).Succeeded);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { Id = "protected-platform", UserName = "protected-platform", FirstName = "Test", LastName = "Operator" };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, RoleNames.PlatformAdministrator)).Succeeded);
            originalPasswordHash = user.PasswordHash;
            database.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = user.Id, Role = RoleNames.Member });
            var member = new Member { ApplicationUserId = user.Id, FullName = "Protected operator" };
            database.Members.Add(member);
            await database.SaveChangesAsync();
            memberId = member.Id;
            Assert.False(await AssociationAccountAccess.CanManageGlobalAccountAsync(database, user.Id, CancellationToken.None));
        }
        using var client = await fixture.ExistingSessionAsync();
        var token = await AuthenticationCookieTests.TokenAsync(client, "/neftyanik/Privacy");
        using var reset = await client.PostAsync($"/neftyanik/Administration/Members/{memberId}/Account/ResetPassword", Form(token,
            ("Input.NewTemporaryPassword", "Replaced123!"), ("Input.ConfirmPassword", "Replaced123!")));
        Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
        await fixture.WithDatabaseAsync(async database =>
            Assert.Equal(originalPasswordHash, (await database.Users.SingleAsync(x => x.Id == "protected-platform")).PasswordHash));
    }

    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] fields) =>
        new(fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)).Prepend(new("__RequestVerificationToken", token)));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PortalWebApplicationFactory _factory = new();
        public WebApplicationFactory<Program> App { get; private set; } = null!;
        public string UserId { get; private set; } = string.Empty;
        public CookieAuthenticationOptions CookieOptions => App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);

        public static async Task<Fixture> CreateAsync(string kind)
        {
            var fixture = new Fixture { UserId = kind };
            fixture.App = AuthenticationCookieTests.CreateCookieApplication(fixture._factory);
            await using var scope = fixture.App.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            database.Associations.Add(new Association { Name = "Demo", Slug = "demo" });
            await database.SaveChangesAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { Id = kind, UserName = kind, Email = kind + "@platform-tests.invalid", FirstName = "Synthetic", LastName = "Test", EmailConfirmed = true, LockoutEnabled = true };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            if (kind is not ("ordinary" or "tenant-admin" or "global-admin" or "claim-only"))
            {
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                Assert.True((await roles.CreateAsync(new IdentityRole(RoleNames.PlatformAdministrator))).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, RoleNames.PlatformAdministrator)).Succeeded);
            }
            if (kind == "global-admin") Assert.True((await users.AddToRoleAsync(user, RoleNames.Administrator)).Succeeded);
            if (kind == "inactive") user.IsActive = false;
            if (kind == "locked") user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            if (kind == "change-password") user.MustChangePassword = true;
            if (kind == "two-factor") user.TwoFactorEnabled = true;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            if (kind == "tenant-admin")
            {
                scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(await database.Associations.SingleAsync(x => x.Slug == "neftyanik"));
                database.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = user.Id, Role = RoleNames.Administrator });
                await database.SaveChangesAsync();
            }
            return fixture;
        }

        public HttpClient Browser(CookieContainer cookies)
        {
            var client = AuthenticationCookieTests.CreateBrowser(App, cookies);
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
            return client;
        }

        public async Task<HttpClient> ExistingSessionAsync(bool forgePlatformClaim = false)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
            var principal = await manager.CreateUserPrincipalAsync((await manager.UserManager.FindByIdAsync(UserId))!);
            if (forgePlatformClaim)
            {
                var identity = (ClaimsIdentity)principal.Identity!;
                identity.AddClaim(new Claim(identity.RoleClaimType, RoleNames.PlatformAdministrator));
                identity.AddClaim(new Claim("dachahub:association-role", RoleNames.PlatformAdministrator));
            }
            var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
            }, IdentityConstants.ApplicationScheme);
            var cookies = new CookieContainer();
            cookies.Add(new Uri("https://localhost"), new Cookie(CookieOptions.Cookie.Name!, CookieOptions.TicketDataFormat.Protect(ticket), "/"));
            return Browser(cookies);
        }

        public async Task<HttpResponseMessage> LoginAsync(HttpClient client, string password = Password, string? returnUrl = null)
        {
            var token = await AuthenticationCookieTests.TokenAsync(client, LoginPath);
            var path = returnUrl is null ? LoginPath : LoginPath + "?ReturnUrl=" + Uri.EscapeDataString(returnUrl);
            return await client.PostAsync(path, Form(token, ("Input.Login", UserId), ("Input.Password", password)));
        }

        public async Task WithDatabaseAsync(Func<ApplicationDbContext, Task> action)
        {
            await using var scope = App.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
