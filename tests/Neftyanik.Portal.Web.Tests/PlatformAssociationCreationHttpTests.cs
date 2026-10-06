using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class PlatformAssociationCreationHttpTests
{
    private const string Page = "/Platform/Associations/Create";
    private static readonly string TemporaryPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA1!";

    [Theory]
    [InlineData(null, HttpStatusCode.Found)]
    [InlineData("member", HttpStatusCode.Forbidden)]
    [InlineData("accountant", HttpStatusCode.Forbidden)]
    [InlineData("tenant-admin", HttpStatusCode.Forbidden)]
    public async Task CreationAndLookup_RequirePlatformPermissionOnHttpAndDirectService(string? user, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync(user);
        using var get = await client.GetAsync(Page);
        Assert.Equal(expected, get.StatusCode);
        var token = await AuthenticationCookieTests.TokenAsync(client, "/neftyanik/Privacy");
        foreach (var handler in new[] { "Preview", "Confirm" })
        {
            using var post = await client.PostAsync(Page + "?handler=" + handler, Form(token,
                ("Input.Name", "Forged"), ("Input.Slug", "forged"), ("Input.AdministratorUserName", "member"), ("ConfirmAdministrator", "true")));
            Assert.Equal(expected, post.StatusCode);
        }
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        try
        {
            accessor.HttpContext = user is null ? null : await fixture.ContextAsync(scope.ServiceProvider, user);
            var creator = scope.ServiceProvider.GetRequiredService<IPlatformAssociationCreator>();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => creator.CreateAsync(new("Forged", "forged", null, null, null, "member", "member", true)));
        }
        finally { accessor.HttpContext = null; }
    }

    [Fact]
    public async Task Create_CreatesSeparateAdministratorAndPreservesExistingAccounts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.AccountSnapshotAsync();
        using var platform = await fixture.ClientAsync("operator");
        var preview = await PreviewAsync(platform, "garden-new");
        Assert.Contains("new-admin", WebUtility.HtmlDecode(preview));
        Assert.Contains("garden-new", preview);
        using var created = await ConfirmAsync(platform, preview, extra: new[]
        {
            ("Input.Slug", "neftyanik"), ("AdministratorUserId", "operator"), ("actorUserId", "member"),
            ("Input.Name", "Forged name"), ("Input.Role", RoleNames.PlatformAdministrator),
            ("Input.AdministratorUserName", "operator"), ("Input.ContactEmail", "forged@example.invalid"),
            ("Input.ContactPhone", "forged-phone"), ("Input.PostalAddress", "forged-address")
        });
        Assert.Equal(HttpStatusCode.Found, created.StatusCode);
        using var details = await platform.GetAsync(created.Headers.Location);
        var detailsHtml = await details.ReadDecodedHtmlAsync();
        Assert.Contains("Товариство створено", detailsHtml);
        Assert.Contains("Перед розрахунками", detailsHtml);
        Assert.Contains("New association", detailsHtml);
        Assert.DoesNotContain("Forged name", detailsHtml);
        Assert.Equal(before, await fixture.AccountSnapshotAsync("new-admin"));
        using var repeat = await ConfirmAsync(platform, preview);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Contains("Такий slug уже використовується", await repeat.ReadDecodedHtmlAsync());

        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(db.IsAssociationResolved);
            var association = await db.Associations.SingleAsync(x => x.Slug == "garden-new");
            Assert.Equal("office@example.invalid", association.ContactEmail);
            Assert.Equal("+380123", association.ContactPhone);
            Assert.Equal("Confirmed address", association.PostalAddress);
            var assignment = Assert.Single(await db.AssociationUserMemberships.IgnoreQueryFilters().Where(x => x.AssociationId == association.Id).ToListAsync());
            var administrator = await db.Users.SingleAsync(x => x.UserName == "new-admin");
            Assert.NotEqual("member", administrator.Id);
            Assert.Equal("shared@example.invalid", administrator.Email);
            Assert.Equal("New administrator", administrator.DisplayName);
            Assert.True(administrator.MustChangePassword);
            Assert.Equal(administrator.Id, assignment.ApplicationUserId);
            Assert.Equal(1, await db.AssociationUserMemberships.IgnoreQueryFilters().CountAsync(x => x.ApplicationUserId == administrator.Id));
            Assert.Equal(association.Id, (await db.AssociationAccountBindings.SingleAsync(x => x.ApplicationUserId == administrator.Id)).AssociationId);
            Assert.Equal(RoleNames.Administrator, assignment.Role);
            Assert.Empty(await db.UserRoles.Where(x => x.UserId == "member").ToListAsync());
            Assert.False(await db.Members.IgnoreQueryFilters().AnyAsync(x => x.AssociationId == association.Id));
            var audit = Assert.Single(await db.PlatformAuditLogs.ToListAsync());
            Assert.Equal("operator", audit.OperatorUserId);
            Assert.Equal(association.Id, audit.AssociationId);
            Assert.DoesNotContain(TemporaryPassword, audit.NewValuesJson);
        }
        foreach (var slug in new[] { "neftyanik", "garden-new" })
        {
            using var denied = await platform.GetAsync($"/{slug}/Administration/Finance");
            Assert.Equal(HttpStatusCode.Found, denied.StatusCode);
            Assert.Contains($"/{slug}/Account/AccessDenied", denied.Headers.Location!.OriginalString);
        }
        using var member = await fixture.ClientAsync("member");
        using var adminPage = await member.GetAsync("/garden-new/Administration");
        Assert.Contains("/garden-new/Account/AccessDenied", adminPage.Headers.Location!.OriginalString);
        using var original = await member.GetAsync("/neftyanik/Member");
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        using var originalAdmin = await member.GetAsync("/neftyanik/Administration/Members/Create");
        Assert.Contains("/neftyanik/Account/AccessDenied", originalAdmin.Headers.Location!.OriginalString);
        using var fresh = await fixture.ClientAsync(null);
        var loginToken = await AuthenticationCookieTests.TokenAsync(fresh, "/garden-new/Account/Login");
        using var login = await fresh.PostAsync("/garden-new/Account/Login", Form(loginToken, ("Input.Login", "new-admin"), ("Input.Password", TemporaryPassword)));
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Assert.Equal("/garden-new/Account/ChangeInitialPassword", login.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("platform")]
    [InlineData("health")]
    [InlineData("error")]
    [InlineData("css")]
    [InlineData("account")]
    [InlineData("custom-assets")]
    public async Task Preview_RejectsServiceAndActualStaticDirectorySlugsBeforePasswordEntry(string slug)
    {
        await using var fixture = await Fixture.CreateAsync();
        var environment = fixture.App.Services.GetRequiredService<IWebHostEnvironment>();
        environment.WebRootFileProvider = new ExtraDirectoryProvider(environment.WebRootFileProvider);
        using var client = await fixture.ClientAsync("operator");
        var preview = await PreviewAsync(client, slug);
        Assert.Contains("Цю адресу зарезервовано", SlugError(preview));
        Assert.Empty(Hidden(preview, "ConfirmationToken"));
        Assert.DoesNotContain("name=\"Password.TemporaryPassword\"", preview);
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Associations.AnyAsync(x => x.Slug == slug));
        Assert.Empty(await db.PlatformAuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData("Test1")]
    [InlineData("two--parts")]
    [InlineData("-start")]
    [InlineData("end-")]
    [InlineData("тест")]
    [InlineData("two words")]
    public async Task Preview_InvalidSlug_ReturnsFieldErrorWithoutIssuingConfirmationOrWritingData(string slug)
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.AccountSnapshotAsync();
        using var client = await fixture.ClientAsync("operator");
        var html = await PreviewAsync(client, slug);
        Assert.Contains("Використовуйте лише малі латинські літери", SlugError(html));
        Assert.Empty(Hidden(html, "ConfirmationToken"));
        Assert.DoesNotContain("name=\"Password.TemporaryPassword\"", html);
        Assert.Contains("value=\"new-admin\"", html);
        Assert.Equal(before, await fixture.AccountSnapshotAsync());
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Associations.CountAsync());
        Assert.Empty(await db.PlatformAuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData("ru-RU", "Используйте только маленькие латинские буквы")]
    [InlineData("uk-UA", "Використовуйте лише малі латинські літери")]
    [InlineData("en-US", "Use only lowercase letters")]
    public async Task Form_EnablesLocalizedClientValidationForSlugAndPasswordConfirmation(string culture, string message)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        using var get = await client.GetAsync(Page);
        var html = await get.ReadDecodedHtmlAsync();
        Assert.Contains("data-val-regex=\"" + message, html);
        Assert.Contains("data-val-regex-pattern=\"[a-z0-9]+(?:-[a-z0-9]+)*\"", html);
        Assert.Contains("data-valmsg-for=\"Input.Slug\"", html);
        Assert.Contains("aria-describedby=\"slug-help slug-error\"", html);
        var jquery = html.IndexOf("/lib/jquery/dist/jquery.min.js", StringComparison.Ordinal);
        var validation = html.IndexOf("/lib/jquery-validation/dist/jquery.validate.min.js", StringComparison.Ordinal);
        var adapters = html.IndexOf("/lib/jquery-validation-unobtrusive/jquery.validate.unobtrusive.min.js", StringComparison.Ordinal);
        Assert.True(jquery >= 0 && validation > jquery && adapters > validation);
        var preview = await PreviewAsync(client, "valid-garden-2");
        Assert.NotEmpty(Hidden(preview, "ConfirmationToken"));
        Assert.Contains("data-val-equalto-other=\"*.TemporaryPassword\"", preview);
        Assert.Contains("data-valmsg-for=\"Password.ConfirmPassword\"", preview);
    }

    [Fact]
    public async Task Confirmation_MismatchedPasswords_ReturnsFieldErrorWithoutEchoingPasswords()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        var preview = await PreviewAsync(client, "password-mismatch");
        using var response = await client.PostAsync(Page + "?handler=Confirm", Form(Hidden(preview, "__RequestVerificationToken"),
            ("ConfirmationToken", Hidden(preview, "ConfirmationToken")), ("ConfirmAdministrator", "true"),
            ("Password.TemporaryPassword", TemporaryPassword), ("Password.ConfirmPassword", TemporaryPassword + "different")));
        var html = await response.ReadDecodedHtmlAsync();
        Assert.Contains("data-valmsg-for=\"Password.ConfirmPassword\"", html);
        Assert.Contains("Введіть і підтвердіть тимчасовий пароль", html);
        Assert.DoesNotContain(TemporaryPassword, html);
        Assert.Equal(Hidden(preview, "ConfirmationToken"), Hidden(html, "ConfirmationToken"));
        await using var scope = fixture.App.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Associations.AnyAsync(x => x.Slug == "password-mismatch"));
    }

    [Fact]
    public async Task Preview_ContainsOnlyNonSecretDataEvenWhenPasswordIsForgedIntoPreviewPost()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        var csrf = await AuthenticationCookieTests.TokenAsync(client, Page);
        using var response = await client.PostAsync(Page + "?handler=Preview", Form(csrf,
            ("Input.Name", "Preview"), ("Input.Slug", "preview"), ("Input.AdministratorUserName", "new-admin"),
            ("Input.AdministratorEmail", "shared@example.invalid"), ("Password.TemporaryPassword", TemporaryPassword),
            ("Password.ConfirmPassword", TemporaryPassword)));
        var html = await response.ReadDecodedHtmlAsync();
        var protector = fixture.App.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("DachaHub.AssociationCreation.Confirmation.v3").ToTimeLimitedDataProtector();
        var payload = protector.Unprotect(Hidden(html, "ConfirmationToken"), out _);
        using var json = JsonDocument.Parse(payload);
        Assert.Equal(new[] { "Input", "OperatorId" }, json.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.DoesNotContain("password", payload, StringComparison.OrdinalIgnoreCase);
        var bytes = System.Text.Encoding.UTF8.GetBytes(TemporaryPassword);
        foreach (var secret in new[] { TemporaryPassword, Convert.ToBase64String(bytes), Convert.ToHexString(bytes), Convert.ToHexString(SHA256.HashData(bytes)) })
        {
            Assert.DoesNotContain(secret, html);
            Assert.DoesNotContain(secret, payload);
        }
        Assert.Equal(string.Empty, Hidden(html, "Password.TemporaryPassword"));
        Assert.DoesNotContain(" / ", html);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Confirmation_RequiresConsentValidProtectedPreviewSameOperatorAndAntiforgery()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        using var noCsrf = await client.PostAsync(Page + "?handler=Preview", Form("", ("Input.Name", "Name"), ("Input.Slug", "no-csrf"), ("Input.AdministratorUserName", "member")));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        var preview = await PreviewAsync(client, "confirmation");
        using var noConsent = await ConfirmAsync(client, preview, consent: false);
        Assert.Contains("Підтвердіть призначення", await noConsent.ReadDecodedHtmlAsync());
        using var invalidToken = await client.PostAsync(Page + "?handler=Confirm", Form(Hidden(preview, "__RequestVerificationToken"), ("ConfirmationToken", "forged"), ("ConfirmAdministrator", "true")));
        Assert.Contains("Повторіть введення", await invalidToken.ReadDecodedHtmlAsync());
        using var other = await fixture.ClientAsync("operator-two");
        var otherCsrf = await AuthenticationCookieTests.TokenAsync(other, Page);
        using var crossOperator = await other.PostAsync(Page + "?handler=Confirm", Form(otherCsrf,
            ("ConfirmationToken", Hidden(preview, "ConfirmationToken")), ("ConfirmAdministrator", "true")));
        Assert.Contains("Повторіть введення", await crossOperator.ReadDecodedHtmlAsync());
        using var noConfirmCsrf = await client.PostAsync(Page + "?handler=Confirm", Form("", ("ConfirmationToken", Hidden(preview, "ConfirmationToken")), ("ConfirmAdministrator", "true")));
        Assert.Equal(HttpStatusCode.BadRequest, noConfirmCsrf.StatusCode);
        await using var scope = fixture.App.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Associations.AnyAsync(x => x.Slug == "confirmation"));
    }

    [Fact]
    public async Task Confirmation_ExpiredTokenCannotCreateAssociation()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        var preview = await PreviewAsync(client, "expired-confirmation");
        var protector = fixture.App.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("DachaHub.AssociationCreation.Confirmation.v3").ToTimeLimitedDataProtector();
        var payload = protector.Unprotect(Hidden(preview, "ConfirmationToken"), out _);
        var expired = protector.Protect(payload, DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Throws<CryptographicException>(() => protector.Unprotect(expired, out _));
        using var response = await client.PostAsync(Page + "?handler=Confirm", Form(Hidden(preview, "__RequestVerificationToken"),
            ("ConfirmationToken", expired), ("ConfirmAdministrator", "true")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Повторіть введення", await response.ReadDecodedHtmlAsync());
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Associations.AnyAsync(x => x.Slug == "expired-confirmation"));
        Assert.Empty(await db.PlatformAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Confirmation_RechecksUsernameAndNeverReusesAccountCreatedAfterPreview()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        var preview = await PreviewAsync(client, "renamed-account");
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.CreateAsync(new ApplicationUser { UserName = "new-admin", Email = "shared@example.invalid" }, TemporaryPassword)).Succeeded);
        }
        var before = await fixture.AccountSnapshotAsync();
        using var response = await ConfirmAsync(client, preview);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Такий логін уже використовується", await response.ReadDecodedHtmlAsync());
        Assert.Equal(before, await fixture.AccountSnapshotAsync());
        await using var verify = fixture.App.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Associations.AnyAsync(x => x.Slug == "renamed-account"));
        Assert.Empty(await db.PlatformAuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData("role")]
    [InlineData("inactive")]
    [InlineData("locked")]
    public async Task Confirmation_RevokedOperatorCannotUsePreviouslyIssuedToken(string reason)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        var preview = await PreviewAsync(client, "revoked-operator");
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync("operator"))!;
            if (reason == "role")
                Assert.True((await users.RemoveFromRoleAsync(user, RoleNames.PlatformAdministrator)).Succeeded);
            else
            {
                if (reason == "inactive") user.IsActive = false;
                if (reason == "locked") { user.LockoutEnabled = true; user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1); }
                Assert.True((await users.UpdateAsync(user)).Succeeded);
            }
        }
        using var response = await ConfirmAsync(client, preview);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var verify = fixture.App.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Associations.AnyAsync(x => x.Slug == "revoked-operator"));
        Assert.Empty(await db.PlatformAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Confirmation_IdentityRejectsPasswordWithoutLeakingItOrLeavingAccount()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        var preview = await PreviewAsync(client, "invalid-password");
        using var response = await ConfirmAsync(client, preview, password: "bad");
        var html = await response.ReadDecodedHtmlAsync();
        Assert.Contains("Identity відхилила", html);
        Assert.DoesNotContain("value=\"bad\"", html);
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Users.AnyAsync(x => x.UserName == "new-admin"));
        Assert.False(await db.Associations.AnyAsync(x => x.Slug == "invalid-password"));
    }

    private static async Task<string> PreviewAsync(HttpClient client, string slug, string username = "new-admin")
    {
        var csrf = await AuthenticationCookieTests.TokenAsync(client, Page);
        using var response = await client.PostAsync(Page + "?handler=Preview", Form(csrf,
            ("Input.Name", "New association"), ("Input.Slug", slug), ("Input.AdministratorUserName", username),
            ("Input.AdministratorEmail", "shared@example.invalid"), ("Input.AdministratorDisplayName", "New administrator"),
            ("Input.ContactEmail", "office@example.invalid"), ("Input.ContactPhone", "+380123"), ("Input.PostalAddress", "Confirmed address")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string preview, bool consent = true, (string Key, string Value)[]? extra = null, string? password = null) =>
        client.PostAsync(Page + "?handler=Confirm", Form(Hidden(preview, "__RequestVerificationToken"),
            new[] { ("ConfirmationToken", Hidden(preview, "ConfirmationToken")), ("ConfirmAdministrator", consent.ToString()),
                ("Password.TemporaryPassword", password ?? TemporaryPassword), ("Password.ConfirmPassword", password ?? TemporaryPassword) }.Concat(extra ?? []).ToArray()));

    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] fields) =>
        new(fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)).Prepend(new("__RequestVerificationToken", token)));
    private static string Hidden(string html, string name) => WebUtility.HtmlDecode(
        Regex.Match(html, $"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    private static string SlugError(string html) => WebUtility.HtmlDecode(
        Regex.Match(html, "<span[^>]*data-valmsg-for=\"Input.Slug\"[^>]*>(.*?)</span>").Groups[1].Value);

    private sealed class ExtraDirectoryProvider(IFileProvider original) : IFileProvider
    {
        public IDirectoryContents GetDirectoryContents(string subpath) => subpath.Length == 0
            ? new Entries(original.GetDirectoryContents(subpath).Append(new DirectoryEntry())) : original.GetDirectoryContents(subpath);
        public IFileInfo GetFileInfo(string subpath) => original.GetFileInfo(subpath);
        public IChangeToken Watch(string filter) => original.Watch(filter);
        private sealed class Entries(IEnumerable<IFileInfo> items) : List<IFileInfo>(items), IDirectoryContents { public bool Exists => true; }
        private sealed class DirectoryEntry : IFileInfo
        {
            public bool Exists => true;
            public long Length => -1;
            public string? PhysicalPath => null;
            public string Name => "custom-assets";
            public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
            public bool IsDirectory => true;
            public Stream CreateReadStream() => throw new NotSupportedException();
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PortalWebApplicationFactory factory = new();
        public WebApplicationFactory<Program> App { get; private set; } = null!;
        public string MemberPassword { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA1!";
        private int initialId;

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.App = AuthenticationCookieTests.CreateCookieApplication(fixture.factory);
            await using var scope = fixture.App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            foreach (var name in new[] { "operator", "operator-two", "member", "accountant", "tenant-admin" })
                Assert.True((await users.CreateAsync(new ApplicationUser { Id = name, UserName = name, Email = "shared@example.invalid", FirstName = "Test", LastName = "Account" }, fixture.MemberPassword)).Succeeded);
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.CreateAsync(new IdentityRole(RoleNames.PlatformAdministrator))).Succeeded);
            foreach (var name in new[] { "operator", "operator-two" })
                Assert.True((await users.AddToRoleAsync((await users.FindByNameAsync(name))!, RoleNames.PlatformAdministrator)).Succeeded);
            var association = await db.Associations.SingleAsync(x => x.Slug == "neftyanik");
            fixture.initialId = association.Id;
            scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(association);
            db.AssociationUserMemberships.AddRange(
                new AssociationUserMembership { ApplicationUserId = "member", Role = RoleNames.Member },
                new AssociationUserMembership { ApplicationUserId = "accountant", Role = RoleNames.Accountant },
                new AssociationUserMembership { ApplicationUserId = "tenant-admin", Role = RoleNames.Administrator });
            await db.SaveChangesAsync();
            return fixture;
        }

        public async Task<DefaultHttpContext> ContextAsync(IServiceProvider services, string user)
        {
            var signIn = services.GetRequiredService<SignInManager<ApplicationUser>>();
            var principal = await signIn.CreateUserPrincipalAsync((await signIn.UserManager.FindByNameAsync(user))!);
            var context = new DefaultHttpContext { RequestServices = services, User = principal };
            context.Request.Path = Page;
            return context;
        }

        public async Task<HttpClient> ClientAsync(string? user)
        {
            var cookies = new CookieContainer();
            if (user is not null)
            {
                await using var scope = App.Services.CreateAsyncScope();
                var context = await ContextAsync(scope.ServiceProvider, user);
                var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
                var ticket = new AuthenticationTicket(context.User, new AuthenticationProperties
                {
                    IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
                }, IdentityConstants.ApplicationScheme);
                cookies.Add(new Uri("https://localhost"), new Cookie(options.Cookie.Name!, options.TicketDataFormat.Protect(ticket), "/") { Secure = true, HttpOnly = true });
            }
            return AuthenticationCookieTests.CreateBrowser(App, cookies);
        }

        public async Task<string> AccountSnapshotAsync(string? excludeLogin = null)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Users = await db.Users.AsNoTracking().Where(x => excludeLogin == null || x.UserName != excludeLogin).OrderBy(x => x.Id).ToListAsync(),
                Roles = await db.UserRoles.AsNoTracking().OrderBy(x => x.UserId).ThenBy(x => x.RoleId).ToListAsync(),
                Memberships = await db.AssociationUserMemberships.IgnoreQueryFilters().AsNoTracking().Where(x => x.AssociationId == initialId).OrderBy(x => x.Id)
                    .Select(x => new { x.Id, x.ApplicationUserId, x.Role, x.IsActive }).ToListAsync(),
                Bootstrap = await db.PlatformBootstrapStates.AsNoTracking().ToListAsync()
            });
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        public async ValueTask DisposeAsync() { await App.DisposeAsync(); await factory.DisposeAsync(); }
    }
}
