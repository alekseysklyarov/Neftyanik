using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class ProductSalesTests
{
    [Theory]
    [InlineData("ru-RU")]
    [InlineData("uk-UA")]
    [InlineData("en-US")]
    public async Task PublicGalleries_OfferRealScreenshotsAndMatchingDemoDestinations(string culture)
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient(cultureName: culture);
        var sources = new HashSet<string>();
        foreach (var page in new[] { "/", "/Product/Management" })
        {
            using var response = await client.GetAsync(page);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(html, "<img[^>]+src=\"([^\"]+/images/demo/[^\"]+|/images/demo/[^\"]+)\""))
                sources.Add(WebUtility.HtmlDecode(match.Groups[1].Value));
        }
        foreach (var screen in new[] { "member", "electricity", "finance", "expenses", "members", "plots" })
        {
            var path = $"/images/demo/{screen}-{culture}.jpg";
            Assert.Contains(path, sources);
            using var image = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
            Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
            var bytes = await image.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.Length > 10_000);
            Assert.Equal(new byte[] { 0xff, 0xd8 }, bytes[..2]);
        }
    }

    [Theory]
    [InlineData("ru-RU", "Портал садового товарищества")]
    [InlineData("uk-UA", "Портал садового товариства")]
    [InlineData("en-US", "Garden association portal")]
    public async Task PublicPages_AreLocalizedAndWorkWithoutAssociation(string culture, string title)
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient(cultureName: culture);
        using var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains(title, await home.ReadDecodedHtmlAsync());
        foreach (var path in new[] { "/Product/Demo", "/Product/Demo?view=accountant", "/Product/Demo?view=member", "/Product/Request" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("name=\"Input.Password\"", await response.Content.ReadAsStringAsync());
        }
        Assert.True(AssociationSlugRules.IsReserved("PRODUCT"));
    }

    [Fact]
    public async Task Submit_PersistsConsentAndRedirectsWithoutLeakingContactData()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient(cultureName: "en-US");
        var fields = ValidFields(await AuthenticationCookieTests.TokenAsync(client, "/Product/Request"));
        using var response = await client.PostAsync("/Product/Request", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Product/Request", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain("contact@example.invalid", response.Headers.Location.OriginalString);
        using var confirmation = await client.GetAsync(response.Headers.Location);
        Assert.Contains("REQUEST SAVED", await confirmation.ReadDecodedHtmlAsync());
        using var refresh = await client.GetAsync(response.Headers.Location);
        Assert.DoesNotContain("REQUEST SAVED", await refresh.ReadDecodedHtmlAsync());
        await factory.ExecuteDbContextAsync(async db =>
        {
            var request = Assert.Single(await db.DemoRequests.ToListAsync());
            Assert.Equal("Example Garden", request.AssociationName);
            Assert.Equal("Alex", request.ContactName);
            Assert.Equal("demo-contact-v1", request.ConsentVersion);
            Assert.Null(request.ContactedAtUtc);
            Assert.Empty(await db.Members.ToListAsync());
        });
    }

    [Theory]
    [InlineData("Input.Consent", "false")]
    [InlineData("Input.Email", "invalid")]
    [InlineData("Input.ContactName", "   ")]
    [InlineData("Input.PlotCount", "-1")]
    [InlineData("Input.PlotCount", "1.5")]
    public async Task InvalidSubmission_DoesNotPersist(string field, string value)
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient(cultureName: "en-US");
        var fields = ValidFields(await AuthenticationCookieTests.TokenAsync(client, "/Product/Request"));
        fields[field] = value;
        using var response = await client.PostAsync("/Product/Request", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
        await factory.ExecuteDbContextAsync(async db => Assert.Empty(await db.DemoRequests.ToListAsync()));
    }

    [Fact]
    public async Task Submit_RejectsMissingCsrfHoneypotAndExcessRequests()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();
        using var noToken = await client.PostAsync("/Product/Request", new FormUrlEncodedContent(ValidFields("")));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        var fields = ValidFields(await AuthenticationCookieTests.TokenAsync(client, "/Product/Request"));
        fields["Website"] = "spam";
        using var bot = await client.PostAsync("/Product/Request", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.BadRequest, bot.StatusCode);
        // Three more posts consume the remaining permits; GET stays available.
        for (var i = 0; i < 3; i++)
        {
            using var post = await client.PostAsync("/Product/Request", new FormUrlEncodedContent(fields));
            Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        }
        using var limited = await client.PostAsync("/Product/Request", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var get = await client.GetAsync("/Product/Request");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        await factory.ExecuteDbContextAsync(async db => Assert.Empty(await db.DemoRequests.ToListAsync()));
    }

    [Theory]
    [InlineData(RoleNames.Member)]
    [InlineData(RoleNames.Accountant)]
    [InlineData(RoleNames.Administrator)]
    public async Task TenantUsers_CannotReadOrUpdateSalesRequests(string role)
    {
        using var factory = new PortalWebApplicationFactory();
        using var app = AuthenticationCookieTests.CreateCookieApplication(factory);
        using var anonymous = AuthenticationCookieTests.CreateBrowser(app, new CookieContainer());
        using var unauthenticated = await anonymous.GetAsync("/Platform/Requests");
        Assert.Equal(HttpStatusCode.Redirect, unauthenticated.StatusCode);
        using var client = await CreateTenantClientAsync(app, role);
        using var denied = await client.GetAsync("/Platform/Requests");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var token = await AuthenticationCookieTests.TokenAsync(client, "/Product/Request");
        using var post = await client.PostAsync("/Platform/Requests?handler=Status", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["id"] = "1", ["contacted"] = "true"
        }));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        foreach (var path in new[] { "/neftyanik/Product/Request", "/neftyanik/Product/Demo", "/neftyanik/Platform/Requests" })
        {
            using var nested = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, nested.StatusCode);
        }
    }

    [Fact]
    public async Task PlatformOperator_CanReadEncodedRequestsAndMarkContactedWithCsrf()
    {
        using var factory = new PortalWebApplicationFactory();
        using var app = AuthenticationCookieTests.CreateCookieApplication(factory);
        var cookies = new CookieContainer();
        long requestId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var request = new DemoRequest { ContactName = "Alex", AssociationName = "<script>alert(1)</script>", Email = "contact@example.invalid", CreatedAtUtc = DateTime.UtcNow };
            db.DemoRequests.Add(request);
            await db.SaveChangesAsync();
            requestId = request.Id;
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = "sales-operator", IsActive = true };
            Assert.True((await users.CreateAsync(user)).Succeeded);
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.CreateAsync(new IdentityRole(RoleNames.PlatformAdministrator))).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, RoleNames.PlatformAdministrator)).Succeeded);
            var principal = await scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>().CreateUserPrincipalAsync(user);
            var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
            var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }, IdentityConstants.ApplicationScheme);
            cookies.Add(new Uri("https://localhost"), new Cookie(options.Cookie.Name!, options.TicketDataFormat.Protect(ticket), "/") { Secure = true, HttpOnly = true });
        }
        using var client = AuthenticationCookieTests.CreateBrowser(app, cookies);
        using var inbox = await client.GetAsync("/Platform/Requests");
        Assert.Equal(HttpStatusCode.OK, inbox.StatusCode);
        Assert.True(inbox.Headers.CacheControl?.NoStore);
        var html = await inbox.Content.ReadAsStringAsync();
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert", html);
        var fields = new Dictionary<string, string> { ["id"] = requestId.ToString(), ["contacted"] = "true" };
        using var noToken = await client.PostAsync("/Platform/Requests?handler=Status", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        fields["__RequestVerificationToken"] = await AuthenticationCookieTests.TokenAsync(client, "/Platform/Requests");
        using var updated = await client.PostAsync("/Platform/Requests?handler=Status", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, updated.StatusCode);
        using var contacted = await client.GetAsync("/Platform/Requests?showContacted=true");
        Assert.Contains($"data-request-id=\"{requestId}\"", await contacted.Content.ReadAsStringAsync());
        await using var check = app.Services.CreateAsyncScope();
        Assert.NotNull((await check.ServiceProvider.GetRequiredService<ApplicationDbContext>().DemoRequests.SingleAsync()).ContactedAtUtc);
    }

    private static async Task<HttpClient> CreateTenantClientAsync(WebApplicationFactory<Program> app, string role)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "tenant-client", IsActive = true };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(await db.Associations.SingleAsync(x => x.Slug == "neftyanik"));
        db.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = user.Id, Role = role });
        await db.SaveChangesAsync();
        var principal = await scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>().CreateUserPrincipalAsync(user);
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }, IdentityConstants.ApplicationScheme);
        var cookies = new CookieContainer();
        cookies.Add(new Uri("https://localhost"), new Cookie(options.Cookie.Name!, options.TicketDataFormat.Protect(ticket), "/") { Secure = true, HttpOnly = true });
        return AuthenticationCookieTests.CreateBrowser(app, cookies);
    }

    private static Dictionary<string, string> ValidFields(string token) => new()
    {
        ["__RequestVerificationToken"] = token, ["Input.ContactName"] = " Alex ",
        ["Input.AssociationName"] = " Example Garden ", ["Input.Email"] = "contact@example.invalid",
        ["Input.PlotCount"] = "120", ["Input.Message"] = "Please show the portal.", ["Input.Consent"] = "true"
    };
}
