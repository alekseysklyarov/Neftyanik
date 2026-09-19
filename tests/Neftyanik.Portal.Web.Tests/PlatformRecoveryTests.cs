using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Identity;
using Neftyanik.Portal.Web.Security;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public sealed class PlatformRecoveryTests
{
    private const string Email = "operator@example.test";
    private const string Forgot = "/Platform/Account/ForgotPassword";
    private const string Reset = "/Platform/Account/ResetPassword";
    private const string Confirm = "/Platform/Account/ConfirmEmail";
    private const string Login = "/Platform/Account/Login";
    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA1!";

    [Fact]
    public async Task EmailOwnership_MustBeProven_BeforeRecovery_AndLinksUseTrustedOrigin()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = fixture.Browser();
        client.DefaultRequestHeaders.Host = "attacker.example";
        using var unavailable = await fixture.RequestAsync(client);
        Assert.Equal(HttpStatusCode.Found, unavailable.StatusCode);
        Assert.Empty(fixture.Mail.Messages);
        using var request = await fixture.RequestAsync(client, confirmation: true);
        var message = Assert.Single(fixture.Mail.Messages);
        Assert.Equal(Email, message.Recipient);
        var link = fixture.Link();
        Assert.Equal("https://trusted.example", link.GetLeftPart(UriPartial.Authority));
        Assert.Empty(link.Query);
        Assert.DoesNotContain("attacker.example", message.Body);
        using var confirmed = await fixture.SubmitLinkAsync(client, Confirm);
        Assert.Equal(HttpStatusCode.Found, confirmed.StatusCode);
        await fixture.UserAsync((_, user) => { Assert.True(user.EmailConfirmed); return Task.CompletedTask; });
        using var reused = await fixture.SubmitLinkAsync(client, Confirm);
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
        using var recovery = await fixture.RequestAsync(client);
        Assert.Equal(2, fixture.Mail.Messages.Count);
    }

    [Fact]
    public async Task Confirmation_SmtpFailureThenResendAndConfirmation_PreserveConsumedMarkerPasswordAndRoles()
    {
        await using var fixture = await Fixture.CreateAsync(delivery: "failure");
        var marker = await fixture.MarkerSnapshotAsync();
        string? passwordHash = null;
        string? stamp = null;
        await fixture.UserAsync((_, user) =>
        {
            passwordHash = user.PasswordHash;
            stamp = user.SecurityStamp;
            Assert.False(user.EmailConfirmed);
            return Task.CompletedTask;
        });
        await fixture.ScopeAsync(services => services.GetRequiredService<IPlatformAccountRecovery>().RequestAsync(Email, confirmEmail: true));
        Assert.Empty(fixture.Mail.Messages);
        Assert.Equal("Platform account email delivery could not be completed.", Assert.Single(fixture.Log.Messages));
        Assert.Equal(marker, await fixture.MarkerSnapshotAsync());
        await fixture.UserAsync((_, user) => { Assert.False(user.EmailConfirmed); return Task.CompletedTask; });

        fixture.Mail.Fail = false;
        var cookies = new CookieContainer();
        using var client = fixture.Browser(cookies);
        using var resent = await fixture.RequestAsync(client, confirmation: true);
        using var unknown = await fixture.RequestAsync(client, confirmation: true, email: "unknown@example.test");
        Assert.Equal(HttpStatusCode.Found, resent.StatusCode);
        Assert.Equal(unknown.Headers.Location, resent.Headers.Location);
        Assert.Equal(Email, Assert.Single(fixture.Mail.Messages).Recipient);
        Assert.Equal("https://trusted.example", fixture.Link().GetLeftPart(UriPartial.Authority));
        Assert.Equal(Confirm, fixture.Link().AbsolutePath);
        Assert.Empty(fixture.Link().Query);
        await fixture.UserAsync((_, user) => { Assert.False(user.EmailConfirmed); return Task.CompletedTask; });

        using var confirmed = await fixture.SubmitLinkAsync(client, Confirm);
        Assert.Equal(HttpStatusCode.Found, confirmed.StatusCode);
        Assert.Equal(Forgot, confirmed.Headers.Location?.OriginalString);
        await fixture.UserAsync(async (users, user) =>
        {
            Assert.True(user.EmailConfirmed);
            Assert.True(user.IsActive);
            Assert.True(user.MustChangePassword);
            Assert.Equal(passwordHash, user.PasswordHash);
            Assert.Equal(stamp, user.SecurityStamp);
            Assert.True(await users.CheckPasswordAsync(user, fixture.Temporary));
            Assert.Equal(new[] { RoleNames.PlatformAdministrator }, await users.GetRolesAsync(user));
        });
        Assert.Equal(marker, await fixture.MarkerSnapshotAsync());
        await fixture.ScopeAsync(async services => Assert.Single(await services.GetRequiredService<ApplicationDbContext>().Users.ToListAsync()));
        var applicationCookieName = fixture.App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme).Cookie.Name;
        Assert.DoesNotContain(cookies.GetAllCookies().Cast<Cookie>(), x => x.Name == applicationCookieName || x.Name == "DachaHub.PlatformOnboarding");
    }

    [Fact]
    public async Task Confirmation_ExpiredTokenCanBeReplaced_EvenAfterTemporaryPasswordExpiration()
    {
        await using var fixture = await Fixture.CreateAsync();
        var marker = await fixture.MarkerSnapshotAsync();
        using var client = fixture.Browser();
        using var requested = await fixture.RequestAsync(client, confirmation: true);
        var original = fixture.LinkValues().Token;
        await fixture.UserAsync(async (users, user) =>
        {
            Assert.True(await users.VerifyUserTokenAsync(user, users.Options.Tokens.EmailConfirmationTokenProvider,
                UserManager<ApplicationUser>.ConfirmEmailTokenPurpose, original));
            Assert.True((await users.SetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider,
                PlatformAdministratorOnboarding.ExpiryToken, DateTimeOffset.UtcNow.AddDays(-2).ToString("O"))).Succeeded);
        });
        var options = fixture.App.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value;
        var protector = fixture.App.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(options.Name);
        var payload = protector.Unprotect(Convert.FromBase64String(original));
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(payload,
            DateTimeOffset.UtcNow.Subtract(options.TokenLifespan).AddMinutes(-1).UtcTicks);
        var expired = Convert.ToBase64String(protector.Protect(payload));
        using var rejected = await fixture.SubmitLinkAsync(client, Confirm, token: expired);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        await fixture.UserAsync((_, user) => { Assert.False(user.EmailConfirmed); return Task.CompletedTask; });

        using var resent = await fixture.RequestAsync(client, confirmation: true);
        Assert.Equal(HttpStatusCode.Found, resent.StatusCode);
        Assert.Equal(2, fixture.Mail.Messages.Count);
        using var stillExpired = await fixture.SubmitLinkAsync(client, Confirm, token: expired);
        Assert.Equal(HttpStatusCode.OK, stillExpired.StatusCode);
        using var confirmed = await fixture.SubmitLinkAsync(client, Confirm);
        Assert.Equal(HttpStatusCode.Found, confirmed.StatusCode);
        using var replay = await fixture.SubmitLinkAsync(client, Confirm);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await fixture.UserAsync(async (users, user) =>
        {
            Assert.True(user.EmailConfirmed);
            Assert.True(user.MustChangePassword);
            Assert.True(await users.CheckPasswordAsync(user, fixture.Temporary));
            Assert.Equal(new[] { RoleNames.PlatformAdministrator }, await users.GetRolesAsync(user));
        });
        Assert.Equal(marker, await fixture.MarkerSnapshotAsync());
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("revoked")]
    [InlineData("locked")]
    [InlineData("confirmed")]
    [InlineData("duplicate-email")]
    public async Task Confirmation_ResendAndRedemptionRecheckEligibility_WithoutRestoringAccess(string state)
    {
        await using var fixture = await Fixture.CreateAsync();
        var marker = await fixture.MarkerSnapshotAsync();
        using var client = fixture.Browser();
        using var requested = await fixture.RequestAsync(client, confirmation: true);
        await fixture.UserAsync(async (users, user) =>
        {
            if (state == "disabled") user.IsActive = false;
            if (state == "confirmed") user.EmailConfirmed = true;
            if (state == "locked") user.LockoutEnd = DateTimeOffset.UtcNow.AddDays(1);
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            if (state == "revoked") Assert.True((await users.RemoveFromRoleAsync(user, RoleNames.PlatformAdministrator)).Succeeded);
            if (state == "duplicate-email") Assert.True((await users.CreateAsync(new ApplicationUser
            {
                UserName = "duplicate", Email = Email, FirstName = "Tenant", LastName = "User"
            }, Secret())).Succeeded);
        });
        using var refused = await fixture.RequestAsync(client, confirmation: true);
        using var unknown = await fixture.RequestAsync(client, confirmation: true, email: "unknown@example.test");
        Assert.Equal(HttpStatusCode.Found, refused.StatusCode);
        Assert.Equal(unknown.Headers.Location, refused.Headers.Location);
        Assert.Single(fixture.Mail.Messages);
        using var rejected = await fixture.SubmitLinkAsync(client, Confirm);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        await fixture.UserAsync(async (users, user) =>
        {
            Assert.Equal(state == "confirmed", user.EmailConfirmed);
            Assert.Equal(state != "disabled", user.IsActive);
            Assert.Equal(state != "revoked", await users.IsInRoleAsync(user, RoleNames.PlatformAdministrator));
            Assert.Equal(state == "locked", await users.IsLockedOutAsync(user));
            Assert.True(user.MustChangePassword);
            Assert.True(await users.CheckPasswordAsync(user, fixture.Temporary));
        });
        Assert.Equal(marker, await fixture.MarkerSnapshotAsync());
    }

    [Fact]
    public async Task Confirmation_TenantAdministratorCannotResendOrRedeemThroughPlatformFlow()
    {
        await using var fixture = await Fixture.CreateAsync();
        var marker = await fixture.MarkerSnapshotAsync();
        var tenant = new ApplicationUser
        {
            UserName = "tenant-admin", Email = "tenant@example.test", EmailConfirmed = false,
            IsActive = true, FirstName = "Tenant", LastName = "Administrator"
        };
        var token = string.Empty;
        await fixture.ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.CreateAsync(tenant, Secret())).Succeeded);
            var database = services.GetRequiredService<ApplicationDbContext>();
            services.GetRequiredService<Neftyanik.Portal.Application.Associations.AssociationContext>()
                .Resolve(await database.Associations.SingleAsync(x => x.Slug == "neftyanik"));
            database.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = tenant.Id, Role = RoleNames.Administrator });
            await database.SaveChangesAsync();
            token = await users.GenerateEmailConfirmationTokenAsync(tenant);
        });
        using var client = fixture.Browser();
        using var refused = await fixture.RequestAsync(client, confirmation: true, email: tenant.Email!);
        using var unknown = await fixture.RequestAsync(client, confirmation: true, email: "unknown@example.test");
        Assert.Equal(HttpStatusCode.Found, refused.StatusCode);
        Assert.Equal(unknown.Headers.Location, refused.Headers.Location);
        Assert.Empty(fixture.Mail.Messages);
        using var rejected = await client.PostAsync(Confirm, Form(await AuthenticationCookieTests.TokenAsync(client, Confirm),
            ("UserId", tenant.Id), ("Token", token)));
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        await fixture.ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(tenant.Id))!;
            Assert.False(user.EmailConfirmed);
            Assert.False(await users.IsInRoleAsync(user, RoleNames.PlatformAdministrator));
        });
        Assert.Equal(marker, await fixture.MarkerSnapshotAsync());
    }

    [Fact]
    public async Task Confirmation_ResendSharesRecoveryRateLimit()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = fixture.Browser();
        for (var i = 0; i < 5; i++)
        {
            using var response = await fixture.RequestAsync(client, confirmation: i % 2 == 0);
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        }
        using var limited = await fixture.RequestAsync(client, confirmation: true);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(3, fixture.Mail.Messages.Count);
        await fixture.UserAsync((_, user) => { Assert.False(user.EmailConfirmed); return Task.CompletedTask; });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_ChangesPasswordOnce_CompletesExpiredInitialOnboarding_AndRejectsOldApplicationCookies(bool pending)
    {
        await using var fixture = await Fixture.CreateAsync(confirmed: true);
        await fixture.UserAsync(async (users, user) => { user.MustChangePassword = false; Assert.True((await users.UpdateAsync(user)).Succeeded); });
        var oldCookies = new CookieContainer();
        using var oldClient = fixture.Browser(oldCookies);
        using var signedIn = await fixture.LoginAsync(oldClient, fixture.Temporary);
        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        using var before = await oldClient.GetAsync("/Platform/");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        string oldStamp = string.Empty;
        await fixture.UserAsync(async (users, user) =>
        {
            oldStamp = user.SecurityStamp!;
            user.MustChangePassword = pending;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            Assert.True((await users.SetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider,
                PlatformAdministratorOnboarding.ExpiryToken, DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))).Succeeded);
        });
        using var client = fixture.Browser();
        using var requested = await fixture.RequestAsync(client);
        var password = Secret();
        using var reset = await fixture.SubmitLinkAsync(client, Reset, password);
        Assert.Equal(HttpStatusCode.Found, reset.StatusCode);
        Assert.Equal(Login, reset.Headers.Location?.OriginalString);
        await fixture.UserAsync(async (users, user) =>
        {
            Assert.False(user.MustChangePassword);
            Assert.NotEqual(oldStamp, user.SecurityStamp);
            Assert.True(await users.CheckPasswordAsync(user, password));
            Assert.False(await users.CheckPasswordAsync(user, fixture.Temporary));
            Assert.Null(await users.GetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider, PlatformAdministratorOnboarding.ExpiryToken));
        });
        using var oldSession = await oldClient.GetAsync("/Platform/");
        Assert.Equal(HttpStatusCode.Found, oldSession.StatusCode);
        Assert.Equal(Login, oldSession.Headers.Location?.OriginalString);
        using var reuse = await fixture.SubmitLinkAsync(client, Reset, Secret());
        Assert.Equal(HttpStatusCode.OK, reuse.StatusCode);
        using var login = await fixture.LoginAsync(client, password);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        using var platform = await client.GetAsync("/Platform/");
        Assert.Equal(HttpStatusCode.OK, platform.StatusCode);
        using var tenant = await client.GetAsync("/neftyanik/Administration");
        Assert.Contains("AccessDenied", tenant.Headers.Location!.OriginalString);
        await fixture.ScopeAsync(async services => Assert.Empty(await services.GetRequiredService<ApplicationDbContext>().AssociationUserMemberships.IgnoreQueryFilters().ToListAsync()));
    }

    [Fact]
    public async Task Recovery_InvalidatesExistingOnboardingCookie()
    {
        await using var fixture = await Fixture.CreateAsync(confirmed: true);
        using var onboarding = fixture.Browser();
        using var login = await fixture.LoginAsync(onboarding, fixture.Temporary);
        Assert.Equal(PlatformOnboardingAuthentication.Page, login.Headers.Location?.OriginalString);
        using var client = fixture.Browser();
        using var requested = await fixture.RequestAsync(client);
        using var reset = await fixture.SubmitLinkAsync(client, Reset, Secret());
        Assert.Equal(HttpStatusCode.Found, reset.StatusCode);
        using var rejected = await onboarding.GetAsync(PlatformOnboardingAuthentication.Page);
        Assert.Equal(Login, rejected.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("expired")]
    [InlineData("email-token")]
    [InlineData("missing-antiforgery")]
    [InlineData("weak-password")]
    public async Task Recovery_InvalidTokensOrRequests_DoNotChangePassword(string failure)
    {
        await using var fixture = await Fixture.CreateAsync(confirmed: true);
        using var client = fixture.Browser();
        using var requested = await fixture.RequestAsync(client);
        var token = fixture.LinkValues().Token;
        if (failure == "invalid") token = "invalid";
        await fixture.UserAsync(async (users, user) =>
        {
            if (failure == "email-token") token = await users.GenerateEmailConfirmationTokenAsync(user);
            if (failure == "expired")
            {
                using var stream = new MemoryStream();
                using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), leaveOpen: true))
                {
                    writer.Write(DateTimeOffset.UtcNow.AddMinutes(-16).UtcTicks);
                    writer.Write(user.Id);
                    writer.Write(PlatformRecoveryTokenProvider.ResetPurpose);
                    writer.Write(user.SecurityStamp!);
                }
                var protector = fixture.App.Services.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector(fixture.App.Services.GetRequiredService<IOptions<PlatformRecoveryTokenOptions>>().Value.Name);
                token = Convert.ToBase64String(protector.Protect(stream.ToArray()));
            }
        });
        Assert.Equal(TimeSpan.FromMinutes(15), fixture.App.Services.GetRequiredService<IOptions<PlatformRecoveryTokenOptions>>().Value.TokenLifespan);
        using var response = await fixture.SubmitLinkAsync(client, Reset, failure == "weak-password" ? "x" : Secret(), token, failure != "missing-antiforgery");
        Assert.Equal(failure == "missing-antiforgery" ? HttpStatusCode.BadRequest : HttpStatusCode.OK, response.StatusCode);
        await fixture.UserAsync(async (users, user) =>
        {
            Assert.True(user.MustChangePassword);
            Assert.True(await users.CheckPasswordAsync(user, fixture.Temporary));
        });
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("revoked")]
    [InlineData("unconfirmed")]
    [InlineData("locked")]
    [InlineData("duplicate-email")]
    public async Task Recovery_RechecksEligibilityAtRedemption_AndNeverRestoresAccess(string state)
    {
        await using var fixture = await Fixture.CreateAsync(confirmed: true);
        using var client = fixture.Browser();
        using var requested = await fixture.RequestAsync(client);
        await fixture.UserAsync(async (users, user) =>
        {
            if (state == "disabled") user.IsActive = false;
            if (state == "unconfirmed") user.EmailConfirmed = false;
            if (state == "locked") user.LockoutEnd = DateTimeOffset.UtcNow.AddDays(1);
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            if (state == "revoked") Assert.True((await users.RemoveFromRoleAsync(user, RoleNames.PlatformAdministrator)).Succeeded);
            if (state == "duplicate-email") Assert.True((await users.CreateAsync(new ApplicationUser { UserName = "tenant", Email = Email, FirstName = "Tenant", LastName = "User" }, Secret())).Succeeded);
        });
        using var denied = await fixture.SubmitLinkAsync(client, Reset, Secret());
        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        using var anotherRequest = await fixture.RequestAsync(client);
        Assert.Single(fixture.Mail.Messages);
        await fixture.UserAsync(async (users, user) => Assert.True(await users.CheckPasswordAsync(user, fixture.Temporary)));
    }

    [Fact]
    public async Task Recovery_UnknownAndTenantEmail_HaveSameResponse_NoEmailOrPrivilegeGrant()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.CreateAsync(new ApplicationUser { UserName = "tenant", Email = "tenant@example.test", EmailConfirmed = true, IsActive = true, FirstName = "Tenant", LastName = "User" }, Secret())).Succeeded);
        });
        using var client = fixture.Browser();
        using var unknown = await fixture.RequestAsync(client, email: "unknown@example.test");
        using var tenant = await fixture.RequestAsync(client, email: "tenant@example.test");
        using var unconfirmed = await fixture.RequestAsync(client);
        Assert.Equal(unknown.StatusCode, tenant.StatusCode);
        Assert.Equal(unknown.Headers.Location, tenant.Headers.Location);
        Assert.Equal(unknown.Headers.Location, unconfirmed.Headers.Location);
        Assert.Empty(fixture.Mail.Messages);
        using var tenantRoute = await client.GetAsync("/neftyanik" + Forgot);
        Assert.Equal(HttpStatusCode.NotFound, tenantRoute.StatusCode);
        await fixture.ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.False(await users.IsInRoleAsync((await users.FindByNameAsync("tenant"))!, RoleNames.PlatformAdministrator));
        });
    }

    [Fact]
    public async Task Recovery_LinkPage_DoesNotReceiveFragmentToken_AndPreventsCachingAndReferrers()
    {
        await using var fixture = await Fixture.CreateAsync(confirmed: true);
        using var client = fixture.Browser();
        using var requested = await fixture.RequestAsync(client);
        var values = fixture.LinkValues();
        using var response = await client.GetAsync(Reset + fixture.Link().Fragment);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(values.Token, html);
        Assert.DoesNotContain(Uri.EscapeDataString(values.Token), html);
        Assert.Contains("platform-recovery.js", html);
        Assert.Contains("data-recovery-form", html);
        Assert.Contains("name=\"Token\"", html);
        Assert.Contains("name=\"UserId\"", html);
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Theory]
    [InlineData(false, "failure")]
    [InlineData(true, "failure")]
    [InlineData(false, "unconfigured")]
    [InlineData(true, "unconfigured")]
    public async Task Recovery_DeliveryUnavailable_RemainsGenericAndDoesNotActivateOrModifyAccount(bool confirmed, string delivery)
    {
        await using var fixture = await Fixture.CreateAsync(confirmed, delivery);
        using var client = fixture.Browser();
        string stamp = string.Empty;
        await fixture.UserAsync((_, user) => { stamp = user.SecurityStamp!; return Task.CompletedTask; });
        using var response = await fixture.RequestAsync(client, confirmation: !confirmed);
        using var unknown = await fixture.RequestAsync(client, confirmation: !confirmed, email: "unknown@example.test");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(unknown.Headers.Location, response.Headers.Location);
        Assert.Empty(fixture.Mail.Messages);
        Assert.Equal("Platform account email delivery could not be completed.", Assert.Single(fixture.Log.Messages));
        await fixture.UserAsync(async (users, user) =>
        {
            Assert.Equal(confirmed, user.EmailConfirmed);
            Assert.True(user.MustChangePassword);
            Assert.Equal(stamp, user.SecurityStamp);
            Assert.True(await users.CheckPasswordAsync(user, fixture.Temporary));
        });
        await fixture.ScopeAsync(async services => Assert.Single(await services.GetRequiredService<ApplicationDbContext>().PlatformBootstrapStates.ToListAsync()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://trusted.example")]
    [InlineData("https://user:secret@trusted.example")]
    [InlineData("https://trusted.example/path")]
    [InlineData("https://trusted.example/?host=attacker.example")]
    [InlineData("https://trusted.example/#fragment")]
    public void Recovery_UntrustedBaseUrl_IsRejected(string baseUrl)
    {
        Assert.Throws<InvalidOperationException>(() => new PlatformRecoveryOptions { BaseUrl = baseUrl }.GetOrigin());
    }

    [Fact]
    public async Task Recovery_RequestsWithAlternateCaseAndTrailingSlash_ShareRateLimit()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = fixture.Browser();
        var csrf = await AuthenticationCookieTests.TokenAsync(client, Forgot);
        for (var i = 0; i < 5; i++)
        {
            using var response = await fixture.RequestAsync(client, email: "unknown@example.test");
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        }
        using var limited = await client.PostAsync(Forgot.ToLowerInvariant() + "/", Form(csrf, ("Email", Email)));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Empty(fixture.Mail.Messages);
    }

    [Fact]
    public async Task Recovery_RequestsAreRateLimited_AndConfirmationRequiresAntiforgery()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = fixture.Browser();
        for (var i = 0; i < 5; i++)
        {
            using var response = await fixture.RequestAsync(client, email: "unknown@example.test");
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        }
        using var limited = await fixture.RequestAsync(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var confirmation = await client.PostAsync(Confirm, Form("", ("UserId", "x"), ("Token", "x")));
        Assert.Equal(HttpStatusCode.BadRequest, confirmation.StatusCode);
        Assert.Empty(fixture.Mail.Messages);
    }

    private static FormUrlEncodedContent Form(string antiforgery, params (string Key, string Value)[] values) =>
        new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)).Prepend(new("__RequestVerificationToken", antiforgery)));

    private sealed class FakeEmailSender : IPlatformEmailSender
    {
        public List<(string Recipient, string Body)> Messages { get; } = [];
        public bool Fail { get; set; }
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("Sensitive transport diagnostics: " + body);
            Messages.Add((recipient, body));
            return Task.CompletedTask;
        }
    }

    private sealed class RecoveryLogger : ILogger<PlatformAccountRecovery>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception);
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PortalWebApplicationFactory _factory = new(additionalConfiguration: new Dictionary<string, string?> { ["PlatformRecovery:BaseUrl"] = "https://trusted.example" });
        public WebApplicationFactory<Program> App { get; private set; } = null!;
        public FakeEmailSender Mail { get; } = new();
        public RecoveryLogger Log { get; } = new();
        public string Temporary { get; } = Secret();

        public static async Task<Fixture> CreateAsync(bool confirmed = false, string delivery = "fake")
        {
            var fixture = new Fixture();
            fixture.Mail.Fail = delivery == "failure";
            fixture.App = AuthenticationCookieTests.CreateCookieApplication(fixture._factory).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPlatformEmailSender>();
                if (delivery == "unconfigured")
                    services.AddSingleton<IPlatformEmailSender>(new SmtpPlatformEmailSender(Options.Create(new PlatformSmtpOptions())));
                else
                    services.AddSingleton<IPlatformEmailSender>(fixture.Mail);
                services.AddSingleton<ILogger<PlatformAccountRecovery>>(fixture.Log);
            }));
            await fixture.ScopeAsync(async services => Assert.Equal(PlatformBootstrapResult.Created,
                await services.GetRequiredService<IPlatformAdministratorOnboarding>().BootstrapAsync("operator", Email, fixture.Temporary)));
            if (confirmed) await fixture.UserAsync(async (users, user) => { user.EmailConfirmed = true; Assert.True((await users.UpdateAsync(user)).Succeeded); });
            return fixture;
        }

        public HttpClient Browser(CookieContainer? cookies = null) => AuthenticationCookieTests.CreateBrowser(App, cookies ?? new CookieContainer());
        public async Task<string> MarkerSnapshotAsync()
        {
            var snapshot = string.Empty;
            await ScopeAsync(async services =>
            {
                var marker = await services.GetRequiredService<ApplicationDbContext>().PlatformBootstrapStates.AsNoTracking().SingleAsync();
                Assert.Equal(PlatformBootstrapDisposition.Consumed, marker.Disposition);
                snapshot = System.Text.Json.JsonSerializer.Serialize(marker);
            });
            return snapshot;
        }
        public Uri Link() => new(Mail.Messages.Last().Body.Split('\n').Single(x => x.StartsWith("https://", StringComparison.Ordinal)));
        public (string UserId, string Token) LinkValues()
        {
            var values = QueryHelpers.ParseQuery(Link().Fragment[1..]);
            return (values["userId"].ToString(), values["token"].ToString());
        }
        public async Task<HttpResponseMessage> RequestAsync(HttpClient client, bool confirmation = false, string email = Email) =>
            await client.PostAsync(Forgot + (confirmation ? "?handler=Confirmation" : ""), Form(await AuthenticationCookieTests.TokenAsync(client, Forgot), ("Email", email)));
        public async Task<HttpResponseMessage> SubmitLinkAsync(HttpClient client, string path, string? password = null, string? token = null, bool antiforgery = true)
        {
            var values = LinkValues();
            var csrf = antiforgery ? await AuthenticationCookieTests.TokenAsync(client, path) : "";
            return await client.PostAsync(path, Form(csrf, ("UserId", values.UserId), ("Token", token ?? values.Token),
                ("NewPassword", password ?? ""), ("ConfirmPassword", password ?? "")));
        }
        public async Task<HttpResponseMessage> LoginAsync(HttpClient client, string password) =>
            await client.PostAsync(Login, Form(await AuthenticationCookieTests.TokenAsync(client, Login), ("Input.Login", "operator"), ("Input.Password", password)));
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
