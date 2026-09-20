using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Identity;
using Neftyanik.Portal.Web.Commands;
using Neftyanik.Portal.Web.Security;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public sealed class PlatformSshRecoveryTests
{
    private const string Login = "/Platform/Account/Login";
    private const string Change = PlatformOnboardingAuthentication.Page;
    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA1!";

    [Fact]
    public async Task CliProvisioning_WithoutSmtpOrConfirmedEmail_RequiresPasswordChangeThenAllowsPlatform()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = fixture.Browser();
        using var login = await fixture.LoginAsync(client, fixture.Temporary);
        Assert.Equal(Change, login.Headers.Location?.OriginalString);
        using var platformBefore = await client.GetAsync("/Platform/");
        Assert.Equal(Login, platformBefore.Headers.Location?.OriginalString);
        using var tenantBefore = await client.GetAsync("/neftyanik/Administration");
        Assert.Contains("/neftyanik/Account/Login", tenantBefore.Headers.Location!.OriginalString);
        using var changed = await fixture.ChangeAsync(client, fixture.Temporary, Secret());
        Assert.Equal(HttpStatusCode.Found, changed.StatusCode);
        using var platformAfter = await client.GetAsync("/Platform/");
        Assert.Equal(HttpStatusCode.OK, platformAfter.StatusCode);
        await fixture.UserAsync((_, user) => { Assert.False(user.EmailConfirmed); Assert.False(user.MustChangePassword); return Task.CompletedTask; });
        Assert.Equal(0, fixture.Mail.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SshReset_PendingOrExpiredInitialPassword_InvalidatesOnboardingCookieAndCompletesWithoutEmail(bool expired)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var previous = fixture.Browser();
        using var login = await fixture.LoginAsync(previous, fixture.Temporary);
        using var pending = await previous.GetAsync(Change);
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        if (expired) await fixture.UserAsync(async (users, user) => Assert.True((await users.SetAuthenticationTokenAsync(user,
            PlatformAdministratorOnboarding.TokenProvider, PlatformAdministratorOnboarding.ExpiryToken, DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))).Succeeded));
        var resetPassword = Secret();
        await fixture.ResetAsync(resetPassword);
        using var rejected = await previous.GetAsync(Change);
        Assert.Equal(Login, rejected.Headers.Location?.OriginalString);
        using var client = fixture.Browser();
        using var oldPassword = await fixture.LoginAsync(client, fixture.Temporary);
        Assert.Equal(HttpStatusCode.OK, oldPassword.StatusCode);
        using var newPassword = await fixture.LoginAsync(client, resetPassword);
        Assert.Equal(Change, newPassword.Headers.Location?.OriginalString);
        using var changed = await fixture.ChangeAsync(client, resetPassword, Secret());
        Assert.Equal(HttpStatusCode.Found, changed.StatusCode);
        using var platform = await client.GetAsync("/Platform/");
        Assert.Equal(HttpStatusCode.OK, platform.StatusCode);
        await fixture.UserAsync((_, user) => { Assert.False(user.EmailConfirmed); Assert.False(user.MustChangePassword); return Task.CompletedTask; });
        Assert.Equal(0, fixture.Mail.Attempts);
    }

    [Fact]
    public async Task SshReset_ForgottenPassword_RejectsOldApplicationTicketEvenAfterOnboardingCompletes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var cookies = new CookieContainer();
        using var previous = fixture.Browser(cookies);
        using var login = await fixture.LoginAsync(previous, fixture.Temporary);
        using var changed = await fixture.ChangeAsync(previous, fixture.Temporary, Secret());
        var options = fixture.App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var oldCookie = cookies.GetCookies(new Uri("https://localhost"))[options.Cookie.Name!]!.Value;
        var ticketWithoutRole = options.TicketDataFormat.Unprotect(oldCookie)!;
        foreach (var identity in ticketWithoutRole.Principal.Identities)
            foreach (var role in identity.FindAll(identity.RoleClaimType).ToList()) identity.RemoveClaim(role);
        var oldCookieWithoutRole = options.TicketDataFormat.Protect(ticketWithoutRole);
        var temporary = Secret();
        await fixture.ResetAsync(temporary);
        using var rejected = await previous.GetAsync("/Platform/");
        Assert.Equal(Login, rejected.Headers.Location?.OriginalString);
        var rolelessCookies = new CookieContainer();
        rolelessCookies.Add(new Uri("https://localhost"), new Cookie(options.Cookie.Name!, oldCookieWithoutRole, "/"));
        using var roleless = fixture.Browser(rolelessCookies);
        using var rejectedOutsidePlatform = await roleless.GetAsync("/neftyanik/Privacy");
        Assert.DoesNotContain(rolelessCookies.GetAllCookies().Cast<Cookie>(), x => x.Name == options.Cookie.Name);
        using var client = fixture.Browser();
        using var reauthenticated = await fixture.LoginAsync(client, temporary);
        using var completed = await fixture.ChangeAsync(client, temporary, Secret());
        Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        var replayCookies = new CookieContainer();
        replayCookies.Add(new Uri("https://localhost"), new Cookie(options.Cookie.Name!, oldCookie, "/"));
        using var replay = fixture.Browser(replayCookies);
        using var replayed = await replay.GetAsync("/Platform/");
        Assert.Equal(Login, replayed.Headers.Location?.OriginalString);
        Assert.Equal(0, fixture.Mail.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResetCli_RejectsRedirectedInputAndPasswordArgumentsBeforeStartup(bool extraArgument)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(PlatformPasswordRecoveryCommand.Name);
        var secret = Secret();
        if (extraArgument) start.ArgumentList.Add("--password=" + secret);
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(1, process.ExitCode);
        Assert.Empty(await output);
        Assert.Contains("requires an interactive terminal", await error);
        Assert.DoesNotContain(secret, await error);
    }

    [Fact]
    public async Task ResetCli_IsNotRegisteredOrRoutedInWebHost()
    {
        await using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();
        using var scope = factory.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<IPlatformAdministratorPasswordRecovery>());
        using var response = await client.PostAsync("/Platform/Account/" + PlatformPasswordRecoveryCommand.Name, new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var services = scope.ServiceProvider;
        var service = new PlatformAdministratorPasswordRecovery(services.GetRequiredService<ApplicationDbContext>(), services.GetRequiredService<UserManager<ApplicationUser>>(), TimeProvider.System);
        Assert.Equal(PlatformOperatorPasswordResetResult.Denied, await service.ResetAsync("operator", "stamp", Secret(), "os-user", "RESET-1"));
    }

    private static FormUrlEncodedContent Form(string csrf, params (string Key, string Value)[] values) =>
        new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)).Prepend(new("__RequestVerificationToken", csrf)));

    private sealed class RejectEmailSender : IPlatformEmailSender
    {
        public int Attempts { get; private set; }
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("No SMTP is configured; no real email was sent.");
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PortalWebApplicationFactory _factory = new(useSqlite: false);
        public WebApplicationFactory<Program> App { get; private set; } = null!;
        public RejectEmailSender Mail { get; } = new();
        public string Temporary { get; } = Secret();
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.App = AuthenticationCookieTests.CreateCookieApplication(fixture._factory).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPlatformEmailSender>();
                services.AddSingleton<IPlatformEmailSender>(fixture.Mail);
            }));
            await fixture.ScopeAsync(async services =>
            {
                await services.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
                Assert.Equal(PlatformBootstrapResult.Created, await services.GetRequiredService<IPlatformAdministratorOnboarding>().BootstrapAsync("operator", "operator@example.test", fixture.Temporary));
            });
            return fixture;
        }
        public HttpClient Browser(CookieContainer? cookies = null) => AuthenticationCookieTests.CreateBrowser(App, cookies ?? new CookieContainer());
        public async Task<HttpResponseMessage> LoginAsync(HttpClient client, string password) =>
            await client.PostAsync(Login, Form(await AuthenticationCookieTests.TokenAsync(client, Login), ("Input.Login", "operator"), ("Input.Password", password)));
        public async Task<HttpResponseMessage> ChangeAsync(HttpClient client, string current, string password) =>
            await client.PostAsync(Change, Form(await AuthenticationCookieTests.TokenAsync(client, Change), ("Input.CurrentPassword", current), ("Input.NewPassword", password), ("Input.ConfirmNewPassword", password)));
        public async Task ResetAsync(string password) => await ScopeAsync(async services =>
        {
            var service = new PlatformAdministratorPasswordRecovery(services.GetRequiredService<ApplicationDbContext>(), services.GetRequiredService<UserManager<ApplicationUser>>(), TimeProvider.System);
            var stamp = await service.GetSecurityStampAsync("operator");
            Assert.NotNull(stamp);
            Assert.Equal(PlatformOperatorPasswordResetResult.Succeeded, await service.ResetAsync("operator", stamp, password, "ssh-user", "RESET-123"));
        });
        public async Task UserAsync(Func<UserManager<ApplicationUser>, ApplicationUser, Task> action) => await ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            await action(users, (await users.FindByNameAsync("operator"))!);
        });
        public async Task ScopeAsync(Func<IServiceProvider, Task> action)
        {
            await using var scope = App.Services.CreateAsyncScope();
            await action(scope.ServiceProvider);
        }
        public async ValueTask DisposeAsync() { await App.DisposeAsync(); await _factory.DisposeAsync(); }
    }
}
