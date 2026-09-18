using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AuthenticationCookieTests
{
    private const string Login = "cookie-test@example.com";
    private const string Password = "CookieTest123!";
    private static readonly Uri Origin = new("https://localhost");

    [Theory]
    [InlineData("neftyanik")]
    [InlineData("second")]
    public async Task LoginThenLogout_UsesStableSecureRootCookieAndEndsSession(string slug)
    {
        using var factory = new PortalWebApplicationFactory();
        using var app = CreateCookieApplication(factory);
        await SeedAsync(app);
        var cookies = new CookieContainer();
        using var client = CreateBrowser(app, cookies);

        using var login = await LoginAsync(client, slug);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        var cookie = Assert.Single(ReadCookies(login).Where(x => x.Name == CookieName(app) && x.Value != string.Empty));
        Assert.Equal("/", cookie.Path.Value);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal("Lax", cookie.SameSite.ToString());
        await AssertAuthenticatedAsync(client, slug);

        using var logout = await LogoutAsync(client, slug);
        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);
        Assert.Equal($"/{slug}", logout.Headers.Location?.OriginalString?.TrimEnd('/'));
        Assert.Contains(ReadCookies(logout), x => x.Name == CookieName(app) && x.Path == "/" && x.Expires < DateTimeOffset.UtcNow);
        await AssertSignedOutEverywhereAsync(app, client, cookies);
    }

    [Theory]
    [InlineData("neftyanik", false)]
    [InlineData("second", false)]
    [InlineData("neftyanik", true)]
    [InlineData("second", true)]
    public async Task Logout_WithExistingRootAndLegacyTenantSessions_RemovesAllApplicationSessions(string slug, bool includeLegacyCookies)
    {
        using var factory = new PortalWebApplicationFactory();
        using var app = CreateCookieApplication(factory);
        await SeedAsync(app);
        var cookies = new CookieContainer();
        await AddExistingSessionAsync(app, cookies, "/");
        if (includeLegacyCookies)
        {
            await AddExistingSessionAsync(app, cookies, "/neftyanik");
            await AddExistingSessionAsync(app, cookies, "/second");
            await AddExistingSessionAsync(app, cookies, "/second-long");
            await AddExistingSessionAsync(app, cookies, "/inactive");
        }
        cookies.Add(Origin, new Cookie("unrelated", "keep", "/"));
        cookies.Add(Origin, new Cookie(CookieName(app) + ".unrelated", "keep", "/neftyanik"));
        using var client = CreateBrowser(app, cookies);
        await AssertAuthenticatedAsync(client, slug);

        using var logout = await LogoutAsync(client, slug);

        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);
        await AssertSignedOutEverywhereAsync(app, client, cookies);
        Assert.Equal("keep", cookies.GetCookies(Origin)["unrelated"]?.Value);
        Assert.Equal("keep", cookies.GetCookies(new Uri(Origin, "/neftyanik/"))[CookieName(app) + ".unrelated"]?.Value);
    }

    [Fact]
    public async Task Login_WithLegacyTenantCookies_ReplacesThemWithSingleRootSession()
    {
        using var factory = new PortalWebApplicationFactory();
        using var app = CreateCookieApplication(factory);
        await SeedAsync(app);
        var cookies = new CookieContainer();
        await AddExistingSessionAsync(app, cookies, "/neftyanik");
        await AddExistingSessionAsync(app, cookies, "/inactive");
        using var client = CreateBrowser(app, cookies);

        using var login = await LoginAsync(client, "second");

        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        var remaining = Assert.Single(cookies.GetAllCookies().Cast<Cookie>().Where(x => x.Name == CookieName(app)));
        Assert.Equal("/", remaining.Path);
        await AssertAuthenticatedAsync(client, "neftyanik");
        await AssertAuthenticatedAsync(client, "second");
        using var logout = await LogoutAsync(client, "neftyanik");
        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);
        await AssertSignedOutEverywhereAsync(app, client, cookies);
    }

    [Fact]
    public async Task Logout_WithChunkedLegacyCookie_DeletesTicketAndVisibleChunks()
    {
        using var factory = new PortalWebApplicationFactory();
        using var app = CreateCookieApplication(factory);
        await SeedAsync(app);
        var cookies = new CookieContainer();
        await AddExistingSessionAsync(app, cookies, "/neftyanik", chunked: true);
        Assert.Contains(cookies.GetAllCookies().Cast<Cookie>(), x => x.Name == CookieName(app) + "C1");
        using var client = CreateBrowser(app, cookies);
        await AssertAuthenticatedAsync(client, "neftyanik");

        using var logout = await LogoutAsync(client, "neftyanik");

        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);
        await AssertSignedOutEverywhereAsync(app, client, cookies);
        Assert.DoesNotContain(cookies.GetAllCookies().Cast<Cookie>(), x => x.Name.StartsWith(CookieName(app) + "C", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Logout_Get_DoesNotEndSession()
    {
        using var factory = new PortalWebApplicationFactory();
        using var app = CreateCookieApplication(factory);
        await SeedAsync(app);
        var cookies = new CookieContainer();
        await AddExistingSessionAsync(app, cookies, "/");
        using var client = CreateBrowser(app, cookies);

        using var response = await client.GetAsync("/neftyanik/Account/Logout");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        await AssertAuthenticatedAsync(client, "neftyanik");
    }

    [Fact]
    public async Task Logout_WithoutAntiforgeryToken_IsRejectedAndKeepsSession()
    {
        using var factory = new PortalWebApplicationFactory();
        using var app = CreateCookieApplication(factory);
        await SeedAsync(app);
        var cookies = new CookieContainer();
        await AddExistingSessionAsync(app, cookies, "/");
        using var client = CreateBrowser(app, cookies);

        using var response = await client.PostAsync("/neftyanik/Account/Logout", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertAuthenticatedAsync(client, "neftyanik");
    }

    internal static WebApplicationFactory<Program> CreateCookieApplication(PortalWebApplicationFactory factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<AuthenticationOptions>(options =>
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme)));

    private static CookieAuthenticationOptions CookieOptions(WebApplicationFactory<Program> app) =>
        app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);

    private static string CookieName(WebApplicationFactory<Program> app) => CookieOptions(app).Cookie.Name!;

    private static async Task SeedAsync(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        database.Associations.AddRange(
            new Association { Name = "Second long", Slug = "second-long", IsActive = true },
            new Association { Name = "Second", Slug = "second", IsActive = true },
            new Association { Name = "Inactive", Slug = "inactive", IsActive = false });
        await database.SaveChangesAsync();
        var result = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(
            new ApplicationUser
            {
                UserName = Login, Email = Login, FirstName = "Cookie", LastName = "Test",
                IsActive = true, MustChangePassword = false
            }, Password);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(x => x.Description)));
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(Login);
        foreach (var slug in new[] { "neftyanik", "second" })
        {
            await using var tenantScope = app.Services.CreateAsyncScope();
            var tenantDatabase = tenantScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var association = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(tenantDatabase.Associations, x => x.Slug == slug);
            tenantScope.ServiceProvider.GetRequiredService<Neftyanik.Portal.Application.Associations.AssociationContext>().Resolve(association);
            tenantDatabase.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = user!.Id, Role = "Member" });
            await tenantDatabase.SaveChangesAsync();
        }
    }

    private static async Task AddExistingSessionAsync(WebApplicationFactory<Program> app, CookieContainer cookies, string path, bool chunked = false)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        var user = await manager.UserManager.FindByNameAsync(Login);
        var principal = await manager.CreateUserPrincipalAsync(user!);
        if (chunked)
        {
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("large-test-claim", new string('x', 6000)));
        }
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        }, IdentityConstants.ApplicationScheme);
        var value = CookieOptions(app).TicketDataFormat.Protect(ticket);
        var context = new DefaultHttpContext();
        new ChunkingCookieManager().AppendResponseCookie(context, CookieName(app), value,
            new CookieOptions { Path = path, Secure = true, HttpOnly = true });
        foreach (var header in context.Response.Headers.SetCookie)
        {
            cookies.SetCookies(Origin, header!);
        }
    }

    internal static HttpClient CreateBrowser(WebApplicationFactory<Program> app, CookieContainer cookies) =>
        new(new BrowserCookieHandler(cookies) { InnerHandler = app.Server.CreateHandler() }) { BaseAddress = Origin };

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string slug)
    {
        var token = await TokenAsync(client, $"/{slug}/Account/Login");
        return await client.PostAsync($"/{slug}/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Login"] = Login,
            ["Input.Password"] = Password,
            ["Input.RememberMe"] = "true"
        }));
    }

    private static async Task<HttpResponseMessage> LogoutAsync(HttpClient client, string slug)
    {
        var token = await TokenAsync(client, $"/{slug}/Privacy");
        return await client.PostAsync($"/{slug}/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        }));
    }

    internal static async Task<string> TokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<value>[^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups["value"].Value);
    }

    private static async Task AssertAuthenticatedAsync(HttpClient client, string slug)
    {
        using var response = await client.GetAsync($"/{slug}/Privacy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("portal-logout-form", await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertSignedOutEverywhereAsync(WebApplicationFactory<Program> app, HttpClient client, CookieContainer cookies)
    {
        Assert.Empty(cookies.GetAllCookies().Cast<Cookie>().Where(x => x.Name == CookieName(app)).Select(x => x.Path));
        foreach (var slug in new[] { "neftyanik", "second" })
        {
            using var response = await client.GetAsync($"/{slug}/Privacy");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("portal-logout-form", await response.Content.ReadAsStringAsync());
            using var protectedPage = await client.GetAsync($"/{slug}/Member");
            Assert.Equal(HttpStatusCode.Found, protectedPage.StatusCode);
            Assert.StartsWith($"https://localhost/{slug}/Account/Login", protectedPage.Headers.Location?.OriginalString);
        }
    }

    private static IList<SetCookieHeaderValue> ReadCookies(HttpResponseMessage response) =>
        SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList());

    private sealed class BrowserCookieHandler(CookieContainer cookies) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var header = cookies.GetCookieHeader(uri);
            if (header.Length > 0)
            {
                request.Headers.Add("Cookie", header);
            }
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var value in values)
                {
                    cookies.SetCookies(uri, value);
                }
            }
            return response;
        }
    }
}
