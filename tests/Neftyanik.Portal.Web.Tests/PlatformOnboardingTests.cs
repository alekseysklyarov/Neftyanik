using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
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
using Neftyanik.Portal.Infrastructure.Identity;
using Neftyanik.Portal.Web.Security;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class PlatformOnboardingTests
{
    private const string Login = "/Platform/Account/Login";
    private const string Change = PlatformOnboardingAuthentication.Page;
    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA1!";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BootstrapCli_RedirectedInputOrUnsupportedArguments_StopsBeforeApplicationStartup(bool extraArgument)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("create-first-platform-admin");
        if (extraArgument) start.ArgumentList.Add("--unsupported");
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(1, process.ExitCode);
        Assert.Empty(await output);
        Assert.Contains("requires an interactive terminal", await error);
        Assert.DoesNotContain("--unsupported", await error);
    }

    [Fact]
    public async Task FirstLogin_OnlyPermitsPasswordChange_ThenIssuesFreshIdentitySession()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = fixture.Browser();
        using var login = await fixture.LoginAsync(client);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Assert.Equal(Change, login.Headers.Location?.OriginalString);
        AssertNoApplicationCookie(fixture);
        var pending = fixture.Cookies.GetCookies(new Uri("https://localhost"))[fixture.PendingOptions.Cookie.Name!]!;
        var oldTicket = pending.Value;
        Assert.True(pending.HttpOnly);
        Assert.True(pending.Secure);
        var ticket = fixture.PendingOptions.TicketDataFormat.Unprotect(oldTicket)!;
        Assert.False(ticket.Properties.IsPersistent);
        Assert.False(ticket.Properties.AllowRefresh ?? true);
        Assert.True(ticket.Properties.ExpiresUtc - ticket.Properties.IssuedUtc <= TimeSpan.FromMinutes(10));
        using var platformBefore = await client.GetAsync("/Platform/");
        Assert.Equal(HttpStatusCode.Found, platformBefore.StatusCode);
        using var forbidden = await client.GetAsync("/neftyanik/Administration/Finance");
        Assert.Equal(HttpStatusCode.Found, forbidden.StatusCode);
        Assert.Contains("/neftyanik/Account/Login", forbidden.Headers.Location!.OriginalString);
        var newPassword = Secret();
        using var changed = await fixture.ChangeAsync(client, newPassword);
        Assert.Equal(HttpStatusCode.Found, changed.StatusCode);
        Assert.Equal("/Platform", changed.Headers.Location?.OriginalString?.TrimEnd('/'));
        Assert.DoesNotContain(fixture.Cookies.GetAllCookies().Cast<Cookie>(), x => x.Name == fixture.PendingOptions.Cookie.Name);
        Assert.Contains(fixture.Cookies.GetAllCookies().Cast<Cookie>(), x => x.Name == fixture.ApplicationOptions.Cookie.Name);
        using var platformAfter = await client.GetAsync("/Platform/");
        Assert.Equal(HttpStatusCode.OK, platformAfter.StatusCode);
        using var tenantAfter = await client.GetAsync("/neftyanik/Administration/Finance");
        Assert.Contains("/neftyanik/Account/AccessDenied", tenantAfter.Headers.Location!.OriginalString);
        await fixture.ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync("first-operator"))!;
            Assert.False(user.MustChangePassword);
            Assert.False(await users.CheckPasswordAsync(user, fixture.Temporary));
            Assert.True(await users.CheckPasswordAsync(user, newPassword));
            Assert.Null(await users.GetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider, PlatformAdministratorOnboarding.ExpiryToken));
            Assert.Empty(await services.GetRequiredService<ApplicationDbContext>().AssociationUserMemberships.IgnoreQueryFilters().ToListAsync());
        });
        var replayCookies = new CookieContainer();
        replayCookies.Add(new Uri("https://localhost"), new Cookie(fixture.PendingOptions.Cookie.Name!, oldTicket, "/"));
        using var replayClient = AuthenticationCookieTests.CreateBrowser(fixture.App, replayCookies);
        using var replay = await replayClient.GetAsync(Change);
        Assert.Equal(HttpStatusCode.Found, replay.StatusCode);
        Assert.Equal(Login, replay.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("expired-password")]
    [InlineData("inactive")]
    [InlineData("locked")]
    [InlineData("revoked")]
    [InlineData("two-factor")]
    public async Task PendingAccount_ExpiredOrRevokedOrUnavailable_DoesNotAuthenticate(string state)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.MutateAsync(state);
        using var client = fixture.Browser();
        using var login = await fixture.LoginAsync(client);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AssertNoApplicationCookie(fixture);
        Assert.DoesNotContain(fixture.Cookies.GetAllCookies().Cast<Cookie>(), x => x.Name == fixture.PendingOptions.Cookie.Name);
    }

    [Fact]
    public async Task InvalidTemporaryPassword_UsesLockoutAndDoesNotIssueSession()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = fixture.Browser();
        for (var i = 0; i < 5; i++)
        {
            using var response = await fixture.LoginAsync(client, Secret());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        await fixture.ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True(await users.IsLockedOutAsync((await users.FindByNameAsync("first-operator"))!));
        });
        using var validButLocked = await fixture.LoginAsync(client);
        Assert.Equal(HttpStatusCode.OK, validButLocked.StatusCode);
        AssertNoApplicationCookie(fixture);
        Assert.DoesNotContain(fixture.Cookies.GetAllCookies().Cast<Cookie>(), x => x.Name == fixture.PendingOptions.Cookie.Name);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("inactive")]
    [InlineData("locked")]
    [InlineData("stamp")]
    [InlineData("expired-password")]
    [InlineData("two-factor")]
    public async Task ExistingOnboardingCookie_RevalidatesCurrentDatabaseState(string state)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = fixture.Browser();
        using var login = await fixture.LoginAsync(client);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        await fixture.MutateAsync(state);
        using var response = await client.GetAsync(Change);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(Login, response.Headers.Location?.OriginalString);
        AssertNoApplicationCookie(fixture);
    }

    [Fact]
    public async Task ExpiredOnboardingTicket_IsNotRenewed()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = fixture.Browser();
        using var login = await fixture.LoginAsync(client);
        var cookie = fixture.Cookies.GetCookies(new Uri("https://localhost"))[fixture.PendingOptions.Cookie.Name!]!;
        var ticket = fixture.PendingOptions.TicketDataFormat.Unprotect(cookie.Value)!;
        ticket.Properties.IssuedUtc = DateTimeOffset.UtcNow.AddMinutes(-20);
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
        cookie.Value = fixture.PendingOptions.TicketDataFormat.Protect(ticket);
        fixture.Cookies.Add(new Uri("https://localhost"), cookie);
        using var response = await client.GetAsync(Change);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(Login, response.Headers.Location?.OriginalString);
        AssertNoApplicationCookie(fixture);
    }

    [Theory]
    [InlineData("missing-token")]
    [InlineData("invalid-token")]
    [InlineData("wrong-current")]
    [InlineData("weak-new")]
    [InlineData("same-password")]
    [InlineData("mismatch")]
    public async Task FailedPasswordChange_NeverClearsRequiredFlagOrCreatesApplicationSession(string failure)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = fixture.Browser();
        using var login = await fixture.LoginAsync(client);
        var token = await AuthenticationCookieTests.TokenAsync(client, Change);
        var current = failure == "wrong-current" ? Secret() : fixture.Temporary;
        var password = failure == "same-password" ? fixture.Temporary : failure == "weak-new" ? new string('x', 1) : Secret();
        using var response = await client.PostAsync(Change, Form(failure == "missing-token" ? "" : failure == "invalid-token" ? "invalid" : token,
            ("Input.CurrentPassword", current), ("Input.NewPassword", password), ("Input.ConfirmNewPassword", failure == "mismatch" ? Secret() : password)));
        Assert.Equal(failure.EndsWith("token", StringComparison.Ordinal) ? HttpStatusCode.BadRequest : HttpStatusCode.OK, response.StatusCode);
        AssertNoApplicationCookie(fixture);
        await fixture.ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync("first-operator"))!;
            Assert.True(user.MustChangePassword);
            Assert.True(await users.CheckPasswordAsync(user, fixture.Temporary));
        });
        var html = await response.Content.ReadAsStringAsync();
        Assert.False(html.Contains(fixture.Temporary, StringComparison.Ordinal));
        Assert.False(html.Contains(password, StringComparison.Ordinal) && failure != "weak-new");
    }

    [Fact]
    public async Task OnboardingCookie_DoesNotUseExistingTenantMembership_OrAcceptApplicationCookie()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            services.GetRequiredService<AssociationContext>().Resolve(await db.Associations.SingleAsync(x => x.Slug == "neftyanik"));
            var user = await db.Users.SingleAsync();
            db.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = user.Id, Role = RoleNames.Administrator });
            await db.SaveChangesAsync();
        });
        using var client = fixture.Browser();
        using var login = await fixture.LoginAsync(client);
        using var tenant = await client.GetAsync("/neftyanik/Administration");
        Assert.Contains("/neftyanik/Account/Login", tenant.Headers.Location!.OriginalString);
        var pending = fixture.Cookies.GetCookies(new Uri("https://localhost"))[fixture.PendingOptions.Cookie.Name!]!;
        var forgedCookies = new CookieContainer();
        forgedCookies.Add(new Uri("https://localhost"), new Cookie(fixture.ApplicationOptions.Cookie.Name!, pending.Value, "/"));
        using var forgedClient = AuthenticationCookieTests.CreateBrowser(fixture.App, forgedCookies);
        using var platform = await forgedClient.GetAsync("/Platform/");
        Assert.Equal(HttpStatusCode.Found, platform.StatusCode);
        using var change = await forgedClient.GetAsync(Change);
        Assert.Equal(HttpStatusCode.Found, change.StatusCode);
    }

    private static void AssertNoApplicationCookie(Fixture fixture) =>
        Assert.DoesNotContain(fixture.Cookies.GetAllCookies().Cast<Cookie>(), x => x.Name == fixture.ApplicationOptions.Cookie.Name);

    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] values) =>
        new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)).Prepend(new("__RequestVerificationToken", token)));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PortalWebApplicationFactory _factory = new();
        public WebApplicationFactory<Program> App { get; private set; } = null!;
        public string Temporary { get; } = Secret();
        public CookieContainer Cookies { get; } = new();
        public CookieAuthenticationOptions PendingOptions => App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(PlatformOnboardingAuthentication.Scheme);
        public CookieAuthenticationOptions ApplicationOptions => App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.App = AuthenticationCookieTests.CreateCookieApplication(fixture._factory);
            await fixture.ScopeAsync(async services => Assert.Equal(PlatformBootstrapResult.Created,
                await services.GetRequiredService<IPlatformAdministratorOnboarding>().BootstrapAsync("first-operator", "operator@example.test", fixture.Temporary)));
            return fixture;
        }

        public HttpClient Browser() => AuthenticationCookieTests.CreateBrowser(App, Cookies);
        public async Task<HttpResponseMessage> LoginAsync(HttpClient client, string? password = null)
        {
            var token = await AuthenticationCookieTests.TokenAsync(client, Login);
            return await client.PostAsync(Login, Form(token, ("Input.Login", "first-operator"), ("Input.Password", password ?? Temporary)));
        }
        public async Task<HttpResponseMessage> ChangeAsync(HttpClient client, string password)
        {
            var token = await AuthenticationCookieTests.TokenAsync(client, Change);
            return await client.PostAsync(Change, Form(token, ("Input.CurrentPassword", Temporary), ("Input.NewPassword", password), ("Input.ConfirmNewPassword", password)));
        }
        public async Task MutateAsync(string state) => await ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync("first-operator"))!;
            if (state == "inactive") user.IsActive = false;
            if (state == "locked") user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            if (state == "two-factor") user.TwoFactorEnabled = true;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            if (state == "stamp") Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded);
            if (state == "revoked") Assert.True((await users.RemoveFromRoleAsync(user, RoleNames.PlatformAdministrator)).Succeeded);
            if (state == "expired-password") Assert.True((await users.SetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider,
                PlatformAdministratorOnboarding.ExpiryToken, DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O", CultureInfo.InvariantCulture))).Succeeded);
        });
        public async Task ScopeAsync(Func<IServiceProvider, Task> action)
        {
            await using var scope = App.Services.CreateAsyncScope();
            await action(scope.ServiceProvider);
        }
        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
