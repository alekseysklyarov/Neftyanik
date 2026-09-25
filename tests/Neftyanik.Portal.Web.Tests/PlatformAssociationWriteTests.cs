using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class PlatformAssociationWriteTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Found)]
    [InlineData("member", HttpStatusCode.Forbidden)]
    [InlineData("accountant", HttpStatusCode.Forbidden)]
    [InlineData("tenant-admin", HttpStatusCode.Forbidden)]
    [InlineData("operator", HttpStatusCode.OK)]
    public async Task GetAndPost_RequireCurrentPlatformPermission(string? user, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync(user);
        foreach (var page in new[] { "Edit", "Status" })
        {
            using var get = await client.GetAsync($"/Platform/Associations/{page}?id={fixture.SecondId}&activate=false");
            Assert.Equal(expected, get.StatusCode);
            var revision = user == "operator" ? Hidden(await get.Content.ReadAsStringAsync(), "Input.Revision") : Guid.Empty.ToString();
            var token = await AuthenticationCookieTests.TokenAsync(client, "/neftyanik/Privacy");
            using var post = await client.PostAsync($"/Platform/Associations/{page}?id={fixture.SecondId}", Form(token,
                ("Input.Name", "Updated"), ("Input.IsActive", "false"), ("Input.Revision", revision)));
            Assert.Equal(user == "operator" ? HttpStatusCode.Found : expected, post.StatusCode);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("member")]
    [InlineData("accountant")]
    [InlineData("tenant-admin")]
    public async Task DirectServiceCalls_RequirePlatformPermission(string? user)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        try
        {
            accessor.HttpContext = user is null ? null : await fixture.ContextAsync(scope.ServiceProvider, user);
            var writer = scope.ServiceProvider.GetRequiredService<IPlatformAssociationWriter>();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.GetAsync(fixture.SecondId));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.UpdateAsync(fixture.SecondId, new("Forged", null, null, null, Guid.Empty)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.SetActiveAsync(fixture.SecondId, false, Guid.Empty));
        }
        finally { accessor.HttpContext = null; }
    }

    [Fact]
    public async Task Edit_IgnoresForgedSlugActorStatusAndOtherTenantFields_AndAuditsRealOperator()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        var token = await AuthenticationCookieTests.TokenAsync(client, $"/Platform/Associations/Edit?id={fixture.SecondId}");
        var fields = new[]
        {
            ("Input.Name", "Updated second"), ("Input.ContactEmail", "office@example.invalid"),
            ("Input.ContactPhone", "+380 123"), ("Input.PostalAddress", "Поштова адреса"),
            ("Input.Revision", Guid.Empty.ToString()), ("Input.Slug", "stolen"), ("Slug", "stolen"),
            ("Input.IsActive", "false"), ("Input.OperatorUserId", "member"), ("actorUserId", "member"),
            ("Input.AssociationId", fixture.ThirdId.ToString()), ("Input.ApplicationUserId", "member")
        };
        using var response = await client.PostAsync($"/Platform/Associations/Edit?id={fixture.SecondId}", Form(token, fields));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/Platform/Associations/Details", response.Headers.Location!.OriginalString);
        using var details = await client.GetAsync(response.Headers.Location);
        var html = await details.ReadDecodedHtmlAsync();
        Assert.Contains("Зміни збережено", html);
        Assert.Contains("office@example.invalid", html);
        using var repeated = await client.PostAsync($"/Platform/Associations/Edit?id={fixture.SecondId}", Form(token, fields));
        Assert.Equal(HttpStatusCode.Found, repeated.StatusCode);
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var association = await database.Associations.AsNoTracking().SingleAsync(x => x.Id == fixture.SecondId);
        Assert.Equal("second", association.Slug);
        Assert.True(association.IsActive);
        Assert.Equal("Third", (await database.Associations.AsNoTracking().SingleAsync(x => x.Id == fixture.ThirdId)).Name);
        var audit = Assert.Single(await database.PlatformAuditLogs.AsNoTracking().ToListAsync());
        Assert.Equal("operator", audit.OperatorUserId);
        Assert.Equal(fixture.SecondId, audit.AssociationId);
        Assert.Equal(PlatformAuditActions.AssociationEdited, audit.Action);
        using var newValues = JsonDocument.Parse(audit.NewValuesJson);
        Assert.Equal(4, newValues.RootElement.EnumerateObject().Count());
        Assert.Equal("Updated second", newValues.RootElement.GetProperty("Name").GetString());
        Assert.False(newValues.RootElement.TryGetProperty("Slug", out _));
        Assert.False(newValues.RootElement.TryGetProperty("IsActive", out _));
    }

    [Fact]
    public async Task Status_DisablesOnlyTargetAndReactivationPreservesExistingMembershipStates()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var platform = await fixture.ClientAsync("operator");
        using var member = await fixture.ClientAsync("member");
        using var disabledMember = await fixture.ClientAsync("disabled-member");
        using var beforeAccess = await member.GetAsync("/second/Member");
        Assert.Equal(HttpStatusCode.OK, beforeAccess.StatusCode);
        var before = await fixture.AccountSnapshotAsync();
        var token = await AuthenticationCookieTests.TokenAsync(platform, $"/Platform/Associations/Status?id={fixture.SecondId}&activate=false");
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.True((await db.Associations.AsNoTracking().SingleAsync(x => x.Id == fixture.SecondId)).IsActive);
            Assert.Empty(await db.PlatformAuditLogs.ToListAsync());
        }
        using var deactivate = await platform.PostAsync($"/Platform/Associations/Status?id={fixture.SecondId}", Form(token,
            ("Input.IsActive", "false"), ("Input.Revision", Guid.Empty.ToString())));
        Assert.Equal(HttpStatusCode.Found, deactivate.StatusCode);
        using var repeat = await platform.PostAsync($"/Platform/Associations/Status?id={fixture.SecondId}", Form(token,
            ("Input.IsActive", "false"), ("Input.Revision", Guid.Empty.ToString())));
        Assert.Equal(HttpStatusCode.Found, repeat.StatusCode);
        using var denied = await member.GetAsync("/second/Member");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var thirdMember = await fixture.ClientAsync("third-member");
        using var other = await thirdMember.GetAsync("/third/Member");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        using var foreignAccess = await member.GetAsync("/third/Member");
        Assert.Contains("/third/Account/AccessDenied", foreignAccess.Headers.Location!.OriginalString);
        using var confirmation = await platform.GetAsync($"/Platform/Associations/Status?id={fixture.SecondId}&activate=true");
        var confirmationHtml = await confirmation.Content.ReadAsStringAsync();
        using var activate = await platform.PostAsync($"/Platform/Associations/Status?id={fixture.SecondId}", Form(
            Hidden(confirmationHtml, "__RequestVerificationToken"), ("Input.IsActive", "true"), ("Input.Revision", Hidden(confirmationHtml, "Input.Revision"))));
        Assert.Equal(HttpStatusCode.Found, activate.StatusCode);
        using var restored = await member.GetAsync("/second/Member");
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        using var stillDisabled = await disabledMember.GetAsync("/second/Member");
        Assert.Equal(HttpStatusCode.Found, stillDisabled.StatusCode);
        Assert.Contains("/second/Account/AccessDenied", stillDisabled.Headers.Location!.OriginalString);
        Assert.Equal(before, await fixture.AccountSnapshotAsync());
        await using var auditScope = fixture.App.Services.CreateAsyncScope();
        var audits = await auditScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PlatformAuditLogs.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(new[] { PlatformAuditActions.AssociationDeactivated, PlatformAuditActions.AssociationActivated }, audits.Select(x => x.Action));
        Assert.All(audits, x => Assert.Equal("operator", x.OperatorUserId));
        Assert.Equal("{\"IsActive\":true}", audits[0].OldValuesJson);
        Assert.Equal("{\"IsActive\":false}", audits[0].NewValuesJson);
        using var finance = await platform.GetAsync("/second/Administration/Finance");
        Assert.Contains("/second/Account/AccessDenied", finance.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Neftyanik_CannotBeDeactivatedEvenWithForgedPost()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        var token = await AuthenticationCookieTests.TokenAsync(client, $"/Platform/Associations/Edit?id={fixture.InitialId}");
        using var response = await client.PostAsync($"/Platform/Associations/Status?id={fixture.InitialId}", Form(token,
            ("Input.IsActive", "false"), ("Input.Revision", Guid.Empty.ToString()), ("Slug", "other")));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True((await database.Associations.SingleAsync(x => x.Id == fixture.InitialId)).IsActive);
        Assert.Empty(await database.PlatformAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Posts_RejectMissingAntiforgeryInvalidInputAndStaleRevision()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        foreach (var page in new[] { "Edit", "Status" })
        {
            using var noToken = await client.PostAsync($"/Platform/Associations/{page}?id={fixture.SecondId}", Form("",
                ("Input.Name", "Forged"), ("Input.IsActive", "false"), ("Input.Revision", Guid.Empty.ToString())));
            Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        }
        var token = await AuthenticationCookieTests.TokenAsync(client, $"/Platform/Associations/Edit?id={fixture.SecondId}");
        foreach (var fields in new[]
        {
            new[] { ("Input.Name", ""), ("Input.Revision", Guid.Empty.ToString()) },
            new[] { ("Input.Name", "Valid"), ("Input.ContactEmail", "invalid"), ("Input.Revision", Guid.Empty.ToString()) },
            new[] { ("Input.Name", new string('x', 201)), ("Input.Revision", Guid.Empty.ToString()) },
            new[] { ("Input.Name", "Valid"), ("Input.ContactPhone", new string('x', 51)), ("Input.Revision", Guid.Empty.ToString()) },
            new[] { ("Input.Name", "Valid"), ("Input.PostalAddress", new string('x', 501)), ("Input.Revision", Guid.Empty.ToString()) }
        })
        {
            using var invalid = await client.PostAsync($"/Platform/Associations/Edit?id={fixture.SecondId}", Form(token, fields));
            Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        }
        using var saved = await client.PostAsync($"/Platform/Associations/Edit?id={fixture.SecondId}", Form(token,
            ("Input.Name", "Winner"), ("Input.Revision", Guid.Empty.ToString())));
        Assert.Equal(HttpStatusCode.Found, saved.StatusCode);
        using var stale = await client.PostAsync($"/Platform/Associations/Status?id={fixture.SecondId}", Form(token,
            ("Input.IsActive", "false"), ("Input.Revision", Guid.Empty.ToString())));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var association = await db.Associations.SingleAsync(x => x.Id == fixture.SecondId);
        Assert.Equal("Winner", association.Name);
        Assert.True(association.IsActive);
        Assert.Single(await db.PlatformAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task RevokedPlatformPermission_PreventsPostWithExistingCookie()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        var token = await AuthenticationCookieTests.TokenAsync(client, $"/Platform/Associations/Edit?id={fixture.SecondId}");
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.RemoveFromRoleAsync((await users.FindByNameAsync("operator"))!, RoleNames.PlatformAdministrator)).Succeeded);
        }
        using var response = await client.PostAsync($"/Platform/Associations/Edit?id={fixture.SecondId}", Form(token,
            ("Input.Name", "Denied"), ("Input.Revision", Guid.Empty.ToString())));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] fields) =>
        new(fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)).Prepend(new("__RequestVerificationToken", token)));

    private static string Hidden(string html, string name) => WebUtility.HtmlDecode(
        Regex.Match(html, $"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PortalWebApplicationFactory factory = new();
        public WebApplicationFactory<Program> App { get; private set; } = null!;
        public int InitialId { get; private set; }
        public int SecondId { get; private set; }
        public int ThirdId { get; private set; }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.App = AuthenticationCookieTests.CreateCookieApplication(fixture.factory);
            await using (var scope = fixture.App.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                fixture.InitialId = await db.Associations.Where(x => x.Slug == "neftyanik").Select(x => x.Id).SingleAsync();
                var second = new Association { Name = "Second", Slug = "second" };
                var third = new Association { Name = "Third", Slug = "third" };
                db.Associations.AddRange(second, third);
                await db.SaveChangesAsync();
                fixture.SecondId = second.Id;
                fixture.ThirdId = third.Id;
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                foreach (var name in new[] { "operator", "member", "accountant", "tenant-admin", "disabled-member" })
                    Assert.True((await users.CreateAsync(new ApplicationUser { Id = name, UserName = name, FirstName = "Test", LastName = "User" })).Succeeded);
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                Assert.True((await roles.CreateAsync(new IdentityRole(RoleNames.PlatformAdministrator))).Succeeded);
                Assert.True((await users.AddToRoleAsync((await users.FindByNameAsync("operator"))!, RoleNames.PlatformAdministrator)).Succeeded);
            }
            foreach (var slug in new[] { "neftyanik", "second", "third" })
            {
                await using var scope = fixture.App.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(await db.Associations.SingleAsync(x => x.Slug == slug));
                var prefix = slug == "second" ? "" : slug + "-";
                if (prefix.Length != 0)
                {
                    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                    foreach (var name in new[] { "member", "accountant", "tenant-admin", "disabled-member" })
                        Assert.True((await users.CreateAsync(new ApplicationUser { Id = prefix + name, UserName = prefix + name })).Succeeded);
                }
                db.AssociationUserMemberships.AddRange(
                    new AssociationUserMembership { ApplicationUserId = prefix + "member", Role = RoleNames.Member },
                    new AssociationUserMembership { ApplicationUserId = prefix + "accountant", Role = RoleNames.Accountant },
                    new AssociationUserMembership { ApplicationUserId = prefix + "tenant-admin", Role = RoleNames.Administrator },
                    new AssociationUserMembership { ApplicationUserId = prefix + "disabled-member", Role = RoleNames.Member, IsActive = false });
                await db.SaveChangesAsync();
            }
            return fixture;
        }

        public async Task<DefaultHttpContext> ContextAsync(IServiceProvider services, string user)
        {
            var signIn = services.GetRequiredService<SignInManager<ApplicationUser>>();
            var principal = await signIn.CreateUserPrincipalAsync((await signIn.UserManager.FindByNameAsync(user))!);
            var context = new DefaultHttpContext { User = principal, RequestServices = services };
            context.Request.Path = "/Platform/Associations/Edit";
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

        public async Task<string> AccountSnapshotAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Users = await db.Users.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                Roles = await db.UserRoles.AsNoTracking().OrderBy(x => x.UserId).ThenBy(x => x.RoleId).ToListAsync(),
                Memberships = await db.AssociationUserMemberships.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
                    .Select(x => new { x.Id, x.AssociationId, x.ApplicationUserId, x.Role, x.IsActive }).ToListAsync(),
                Bootstrap = await db.PlatformBootstrapStates.AsNoTracking().ToListAsync()
            });
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await factory.DisposeAsync();
        }
    }
}
