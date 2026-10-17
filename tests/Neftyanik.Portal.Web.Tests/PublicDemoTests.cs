using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Demo;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class PublicDemoTests
{
    private const string Prefix = PublicDemo.Prefix;

    [Theory]
    [InlineData("member")]
    [InlineData("electricity")]
    [InlineData("finance")]
    [InlineData("expenses")]
    [InlineData("members")]
    [InlineData("plots")]
    public async Task Screens_OpenWithRealCookieAndCorrectRole(string screen)
    {
        using var host = new DemoHost();
        using var client = host.Browser(out var cookies);
        var path = await Enter(client, screen);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Test association", await response.ReadDecodedHtmlAsync());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Contains(cookies.GetAllCookies().Cast<Cookie>(), c => c.Name == "DachaHub.Demo" && c.HttpOnly);
        foreach (var forbidden in new[] { "/neftyanik/Member", "/Platform/Requests" })
            Assert.Equal(HttpStatusCode.Found, (await client.GetAsync(forbidden)).StatusCode);
        foreach (var forbidden in new[] { "/Platform/Requests", "/Product/Request" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Prefix + forbidden)).StatusCode);
    }

    [Fact]
    public async Task RoleSwitch_PreservesSessionAndEnforcesPagePermissions()
    {
        using var host = new DemoHost();
        using var client = host.Browser(out var cookies);
        await Enter(client, "member");
        var id = host.SessionId(cookies);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Prefix + "/Administration/Finance")).StatusCode);
        await Enter(client, "finance");
        Assert.Equal(id, host.SessionId(cookies));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Prefix + "/Administration/Plots/Create")).StatusCode);
        await Enter(client, "members");
        Assert.Equal(id, host.SessionId(cookies));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Prefix + "/Administration/Plots/Create")).StatusCode);
    }

    [Fact]
    public async Task Expenses_AreEditablePrivateAndResettable_WithoutChangingNormalDatabase()
    {
        using var host = new DemoHost();
        using var first = host.Browser(out var firstCookies);
        using var second = host.Browser(out var secondCookies);
        await Enter(first, "expenses");
        await Enter(second, "expenses");
        var firstId = host.SessionId(firstCookies);
        Assert.NotEqual(firstId, host.SessionId(secondCookies));
        var path = Prefix + "/Administration/Finance/Expenses/Create?culture=en-US";
        using var result = await Post(first, path, new()
        {
            ["Input.ExpenseCategoryId"] = "2", ["Input.ExpenseDate"] = DateTime.Today.ToString("yyyy-MM-dd"),
            ["Input.Amount"] = "125.50", ["Input.PaymentMethod"] = "1", ["Input.FundingSource"] = "1",
            ["Input.Description"] = "Private demo expense"
        });
        Assert.Equal(HttpStatusCode.Found, result.StatusCode);
        await host.DemoDb(firstCookies, async db => Assert.Equal(125.50m, (await db.Expenses.SingleAsync(e => e.Description == "Private demo expense")).Amount));
        await host.DemoDb(secondCookies, async db => Assert.False(await db.Expenses.AnyAsync(e => e.Description == "Private demo expense")));
        await host.NormalDb(async db => { Assert.Empty(await db.Expenses.ToListAsync()); Assert.Empty(await db.Members.ToListAsync()); });
        await Enter(first, "expenses", reset: true);
        Assert.NotEqual(firstId, host.SessionId(firstCookies));
        Assert.Null(host.App.Services.GetRequiredService<DemoWorkspaceStore>().Acquire(firstId));
        await host.DemoDb(firstCookies, async db => Assert.Single(await db.Expenses.ToListAsync()));
    }

    [Fact]
    public async Task Member_CanSubmitReading_AndCannotEditAnotherMembersMeter()
    {
        using var host = new DemoHost();
        using var client = host.Browser(out var cookies);
        await Enter(client, "electricity");
        var path = Prefix + "/Member/Electricity/Meters/1/Readings/Create?culture=en-US";
        using var result = await Post(client, path, new()
        {
            ["Input.ReadingDate"] = DateTime.Today.ToString("yyyy-MM-dd"), ["Input.CurrentReading"] = "1100"
        });
        Assert.Equal(HttpStatusCode.Found, result.StatusCode);
        await host.DemoDb(cookies, async db =>
        {
            var reading = await db.MemberElectricityReadings.SingleAsync(r => r.MemberElectricityMeterId == 1 && r.CurrentReading == 1100);
            Assert.True(reading.SubmittedByMember);
            Assert.Equal(86.40m, reading.Amount);
            Assert.NotNull(reading.ChargeId);
        });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path.Replace("/Meters/1/", "/Meters/2/"))).StatusCode);
    }

    [Fact]
    public async Task Administrator_CanCreatePlotInDemo()
    {
        using var host = new DemoHost();
        using var client = host.Browser(out var cookies);
        await Enter(client, "plots");
        using var result = await Post(client, Prefix + "/Administration/Plots/Create?culture=en-US", new()
        {
            ["Input.Number"] = "DEMO-NEW", ["Input.AreaSquareMeters"] = "6", ["Input.IsActive"] = "true"
        });
        Assert.Equal(HttpStatusCode.Found, result.StatusCode);
        await host.DemoDb(cookies, async db => Assert.True(await db.Plots.AnyAsync(p => p.Number == "DEMO-NEW")));
        await host.NormalDb(async db => Assert.Empty(await db.Plots.ToListAsync()));
    }

    [Fact]
    public async Task ExpiryAtThirtyMinutes_ClosesOldSessionAndStartsWithOriginalData()
    {
        using var host = new DemoHost();
        using var client = host.Browser(out var cookies);
        await Enter(client, "member");
        var id = host.SessionId(cookies);
        host.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Prefix + "/Member")).StatusCode);
        await Enter(client, "finance");
        Assert.Equal(id, host.SessionId(cookies));
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        using var expired = await client.GetAsync(Prefix + "/Administration/Finance");
        Assert.Equal(HttpStatusCode.Found, expired.StatusCode);
        Assert.Equal("/Product/Demo?expired=true", expired.Headers.Location!.OriginalString);
        Assert.Null(host.App.Services.GetRequiredService<DemoWorkspaceStore>().Acquire(id));
        await Enter(client, "member");
        Assert.NotEqual(id, host.SessionId(cookies));
    }

    [Fact]
    public async Task EntryAndMutation_RequireAntiforgery_AndRejectArbitraryDestinations()
    {
        using var host = new DemoHost();
        using var client = host.Browser(out _);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Product/Demo?handler=Enter", new FormUrlEncodedContent(new Dictionary<string, string> { ["screen"] = "member" }))).StatusCode);
        var fields = new Dictionary<string, string> { ["screen"] = "https://example.com", ["__RequestVerificationToken"] = await AuthenticationCookieTests.TokenAsync(client, "/Product/Demo") };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Product/Demo?handler=Enter", new FormUrlEncodedContent(fields))).StatusCode);
        await Enter(client, "expenses");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Prefix + "/Administration/Finance/Expenses/Create", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
    }

    [Fact]
    public async Task DemoLogout_PreservesNormalLogin_AndEndsOnlyDemoAccess()
    {
        using var host = new DemoHost();
        using var client = host.Browser(out var cookies);
        await host.NormalDb(async db =>
        {
            db.Users.Add(new ApplicationUser { Id = "real-test", UserName = "real-test", FirstName = "Normal", LastName = "Account", IsActive = true, SecurityStamp = "demo-test-normal-stamp" });
            db.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = "real-test", Role = "Member" });
            await db.SaveChangesAsync();
        });
        var options = host.App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "real-test"), new Claim(ClaimTypes.Name, "real-test"), new Claim("AspNet.Identity.SecurityStamp", "demo-test-normal-stamp") }, IdentityConstants.ApplicationScheme)), new AuthenticationProperties { IssuedUtc = host.Clock.GetUtcNow(), ExpiresUtc = host.Clock.GetUtcNow().AddHours(1) }, IdentityConstants.ApplicationScheme);
        cookies.Add(new Uri("https://localhost"), new Cookie(options.Cookie.Name!, options.TicketDataFormat.Protect(ticket), "/"));
        await Enter(client, "member");
        var token = await AuthenticationCookieTests.TokenAsync(client, Prefix + "/Member");
        using var logout = await client.PostAsync(Prefix + "/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);
        Assert.DoesNotContain(cookies.GetAllCookies().Cast<Cookie>(), c => c.Name == "DachaHub.Demo");
        Assert.Contains(cookies.GetAllCookies().Cast<Cookie>(), c => c.Name == options.Cookie.Name);
        Assert.Equal(HttpStatusCode.Found, (await client.GetAsync(Prefix + "/Member")).StatusCode);
        using var normal = await client.GetAsync("/neftyanik/Privacy");
        Assert.Contains("portal-logout-form", await normal.Content.ReadAsStringAsync());
    }

    private static async Task<string> Enter(HttpClient client, string screen, bool reset = false)
    {
        var token = await AuthenticationCookieTests.TokenAsync(client, "/Product/Demo?culture=en-US");
        using var response = await client.PostAsync("/Product/Demo?handler=Enter&culture=en-US", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["screen"] = screen, ["reset"] = reset.ToString(), ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith(Prefix + "/", response.Headers.Location!.OriginalString);
        return response.Headers.Location.OriginalString;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await AuthenticationCookieTests.TokenAsync(client, path);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private sealed class DemoHost : IDisposable
    {
        private readonly PortalWebApplicationFactory factory = new();
        public AdjustableClock Clock { get; } = new();
        public WebApplicationFactory<Program> App { get; }
        public DemoHost() => App = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            PublicDemo.UseScopedDatabase(services);
            services.AddSingleton<TimeProvider>(Clock);
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = PublicDemo.SelectorScheme;
                options.DefaultChallengeScheme = PublicDemo.SelectorScheme;
                options.DefaultForbidScheme = PublicDemo.SelectorScheme;
            });
            services.PostConfigure<CookieAuthenticationOptions>(PublicDemo.Scheme, options => options.TimeProvider = Clock);
        }));
        public HttpClient Browser(out CookieContainer cookies) => AuthenticationCookieTests.CreateBrowser(App, cookies = new());
        public string SessionId(CookieContainer cookies)
        {
            var cookie = cookies.GetCookies(new Uri("https://localhost"))["DachaHub.Demo"]!;
            return App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(PublicDemo.Scheme).TicketDataFormat.Unprotect(cookie.Value)!.Principal.FindFirstValue(PublicDemo.SessionClaim)!;
        }
        public async Task DemoDb(CookieContainer cookies, Func<ApplicationDbContext, Task> action)
        {
            using var lease = App.Services.GetRequiredService<DemoWorkspaceStore>().Acquire(SessionId(cookies));
            Assert.NotNull(lease);
            var association = new AssociationContext();
            association.Resolve(lease.Workspace.Association);
            await using var db = new ApplicationDbContext(DemoData.Options(lease.Workspace.ConnectionString), association);
            await action(db);
        }
        public async Task NormalDb(Func<ApplicationDbContext, Task> action)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(await db.Associations.SingleAsync(a => a.Slug == "neftyanik"));
            await action(db);
        }
        public void Dispose() { App.Dispose(); factory.Dispose(); }
    }

    private sealed class AdjustableClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
