using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class SalesPilotCardTests
{
    [Fact]
    public async Task Save_PreservesPilotDatesAndRejectsStaleEditsAndMissingCsrf()
    {
        using var factory = new PortalWebApplicationFactory();
        using var app = AuthenticationCookieTests.CreateCookieApplication(factory);
        using var client = await CreateOperatorAsync(app);
        var fields = Fields(await AuthenticationCookieTests.TokenAsync(client, "/Platform/Requests/Details/1"));
        var noToken = new Dictionary<string, string>(fields);
        noToken.Remove("__RequestVerificationToken");
        using var rejected = await client.PostAsync("/Platform/Requests/Details/1", new FormUrlEncodedContent(noToken));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var saved = await client.PostAsync("/Platform/Requests/Details/1", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        await WithDatabaseAsync(app, async db =>
        {
            var request = await db.DemoRequests.SingleAsync();
            Assert.Equal("pilot", request.PilotStage);
            Assert.Equal(new DateOnly(2026, 10, 22), request.PilotStartDate);
            Assert.Equal(new DateOnly(2027, 1, 22), request.PilotEndDate);
            Assert.Equal("Agreed scope", request.PilotNotes);
            Assert.Equal("Review opening balances", request.NextAction);
            Assert.Equal(new DateOnly(2026, 10, 23), request.NextActionDate);
            Assert.NotNull(request.ContactedAtUtc);
            Assert.NotEqual(Guid.Empty, request.PilotRevision);
        });
        fields["Input.Notes"] = "Stale edit";
        using var stale = await client.PostAsync("/Platform/Requests/Details/1", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await WithDatabaseAsync(app, async db => Assert.Equal("Agreed scope", (await db.DemoRequests.SingleAsync()).PilotNotes));
        // A quick status change must not discard an active pilot's stage or dates.
        using var move = await client.PostAsync("/Platform/Requests?handler=Status", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = fields["__RequestVerificationToken"], ["id"] = "1", ["contacted"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, move.StatusCode);
        Assert.Contains("/Platform/Requests/Details/1", move.Headers.Location!.OriginalString);
        await WithDatabaseAsync(app, async db => Assert.NotNull((await db.DemoRequests.SingleAsync()).ContactedAtUtc));
    }

    [Theory]
    [InlineData("Input.Stage", "unknown")]
    [InlineData("Input.StartDate", "")]
    [InlineData("Input.EndDate", "2026-10-21")]
    [InlineData("Input.NextAction", "")]
    [InlineData("Input.NextActionDate", "invalid")]
    [InlineData("Input.Revision", "invalid")]
    [InlineData("Input.Notes", "overlong")]
    public async Task InvalidCard_DoesNotChangeStoredInquiry(string key, string value)
    {
        using var factory = new PortalWebApplicationFactory();
        using var app = AuthenticationCookieTests.CreateCookieApplication(factory);
        using var client = await CreateOperatorAsync(app);
        var fields = Fields(await AuthenticationCookieTests.TokenAsync(client, "/Platform/Requests/Details/1"));
        fields[key] = value == "overlong" ? new string('x', 4001) : value;
        using var response = await client.PostAsync("/Platform/Requests/Details/1", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
        await WithDatabaseAsync(app, async db =>
        {
            var request = await db.DemoRequests.SingleAsync();
            Assert.Equal("new", request.PilotStage);
            Assert.Null(request.PilotNotes);
            Assert.Null(request.ContactedAtUtc);
            Assert.Equal(Guid.Empty, request.PilotRevision);
        });
    }

    private static async Task WithDatabaseAsync(WebApplicationFactory<Program> app, Func<ApplicationDbContext, Task> action)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private static Dictionary<string, string> Fields(string token) => new()
    {
        ["__RequestVerificationToken"] = token, ["Input.Stage"] = "pilot",
        ["Input.Revision"] = Guid.Empty.ToString(), ["Input.Notes"] = " Agreed scope ",
        ["Input.StartDate"] = "2026-10-22", ["Input.EndDate"] = "2027-01-22",
        ["Input.NextAction"] = "Review opening balances", ["Input.NextActionDate"] = "2026-10-23"
    };

    private static async Task<HttpClient> CreateOperatorAsync(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.DemoRequests.Add(new DemoRequest { Id = 1, ContactName = "Alex", AssociationName = "Garden", Email = "example@example.invalid", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "pilot-operator", IsActive = true };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        Assert.True((await roles.CreateAsync(new IdentityRole(RoleNames.PlatformAdministrator))).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, RoleNames.PlatformAdministrator)).Succeeded);
        var principal = await scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>().CreateUserPrincipalAsync(user);
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }, IdentityConstants.ApplicationScheme);
        var cookies = new CookieContainer();
        cookies.Add(new Uri("https://localhost"), new Cookie(options.Cookie.Name!, options.TicketDataFormat.Protect(ticket), "/") { Secure = true, HttpOnly = true });
        return AuthenticationCookieTests.CreateBrowser(app, cookies);
    }
}
