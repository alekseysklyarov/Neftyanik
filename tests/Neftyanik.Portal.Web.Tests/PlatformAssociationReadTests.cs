using System.Net;
using System.Security.Claims;
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

public class PlatformAssociationReadTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Found)]
    [InlineData("member", HttpStatusCode.Forbidden)]
    [InlineData("accountant", HttpStatusCode.Forbidden)]
    [InlineData("tenant-admin", HttpStatusCode.Forbidden)]
    [InlineData("operator", HttpStatusCode.OK)]
    public async Task Pages_RequirePlatformAdministratorWithRealIdentityCookie(string? user, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync(user);
        foreach (var path in new[] { "/Platform/Associations", $"/Platform/Associations/Details?id={fixture.SleepingId}" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(expected, response.StatusCode);
            if (user is null)
            {
                Assert.Equal("/Platform/Account/Login", response.Headers.Location?.OriginalString);
            }
        }
    }

    [Fact]
    public async Task List_CountsMembersRatherThanRolesAndIncludesInactiveAssociationAdministrators()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        using var response = await client.GetAsync("/Platform/Associations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.ReadDecodedHtmlAsync();
        Assert.Contains("Товариства", html);
        Assert.Contains("Нефтяник", html);
        Assert.Contains("Літній сад", html);
        Assert.Contains("Спляче товариство", html);
        var row = Regex.Match(html, $"<tr data-association-id=\"{fixture.SleepingId}\">(.*?)</tr>", RegexOptions.Singleline).Value;
        Assert.Contains("Неактивне", row);
        Assert.Contains("data-member-count=\"3\"", row);
        Assert.Contains("data-administrator-count=\"2\"", row);
        Assert.Contains($"/Platform/Associations/Details?id={fixture.SleepingId}", row);
        Assert.DoesNotContain("private-personal-value", html);
        using var dashboard = await client.GetAsync("/Platform");
        Assert.Contains("/Platform/Associations", await dashboard.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("?Search=sleeping", "Спляче товариство", "Літній сад")]
    [InlineData("?Search=%D0%9B%D1%96%D1%82%D0%BD%D1%96%D0%B9", "Літній сад", "Спляче товариство")]
    [InlineData("?IsActive=false", "Спляче товариство", "Нефтяник")]
    [InlineData("?IsActive=true", "Літній сад", "Спляче товариство")]
    public async Task List_FiltersByNameSlugAndStatus(string query, string included, string excluded)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        using var response = await client.GetAsync("/Platform/Associations" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.ReadDecodedHtmlAsync();
        Assert.Contains(included, html);
        Assert.DoesNotContain(excluded, html);
    }

    [Fact]
    public async Task List_PaginatesOnServerAndPreservesFilters()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            database.Associations.AddRange(Enumerable.Range(1, 23).Select(i =>
                new Association { Name = $"Paged {i:00}", Slug = $"paged-{i}", IsActive = false }));
            await database.SaveChangesAsync();
        }
        using var client = await fixture.ClientAsync("operator");
        using var first = await client.GetAsync("/Platform/Associations?Search=Paged&IsActive=false");
        var html = await first.ReadDecodedHtmlAsync();
        Assert.Equal(20, Regex.Matches(html, "<tr data-association-id=").Count);
        Assert.Contains("Paged 01", html);
        Assert.DoesNotContain("Paged 21", html);
        Assert.Contains("search=Paged", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("isActive=False", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pageNumber=2", html, StringComparison.OrdinalIgnoreCase);
        using var last = await client.GetAsync("/Platform/Associations?Search=Paged&IsActive=false&PageNumber=2147483647");
        var lastHtml = await last.ReadDecodedHtmlAsync();
        Assert.Equal(3, Regex.Matches(lastHtml, "<tr data-association-id=").Count);
        Assert.Contains("Paged 23", lastHtml);
        using var negative = await client.GetAsync("/Platform/Associations?Search=Paged&PageNumber=-1");
        Assert.Contains("Paged 01", await negative.ReadDecodedHtmlAsync());
        using var empty = await client.GetAsync("/Platform/Associations?Search=no-matches");
        Assert.Contains("Товариства не знайдено", await empty.ReadDecodedHtmlAsync());
        using var invalid = await client.GetAsync("/Platform/Associations?IsActive=invalid");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Details_ShowsOnlyRequiredAdministratorFieldsAndHandlesMissingAndEmptyAssociations()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        using var response = await client.GetAsync($"/Platform/Associations/Details?id={fixture.SleepingId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.ReadDecodedHtmlAsync();
        Assert.Contains("sleeping-admin", html);
        Assert.Contains("blocked-admin", html);
        Assert.Contains("former-admin", html);
        Assert.Contains("Вимкнене", html);
        Assert.Contains("Заблокований", html);
        Assert.Contains("Неактивний", html);
        Assert.Contains("Дата створення (UTC)", html);
        Assert.Contains("data-member-count=\"3\"", html);
        Assert.DoesNotContain("private-personal-value", html);
        Assert.DoesNotContain("tenant-admin", html);
        Assert.DoesNotContain("SecurityStamp", html);
        Assert.DoesNotContain("PasswordHash", html);
        using var empty = await client.GetAsync($"/Platform/Associations/Details?id={fixture.SummerId}");
        Assert.Contains("Адміністраторів не призначено", await empty.ReadDecodedHtmlAsync());
        using var missing = await client.GetAsync("/Platform/Associations/Details?id=2147483647");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Read_DoesNotGrantTenantFinanceAccessOrChangeGlobalState()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();
        using var client = await fixture.ClientAsync("operator");
        using var list = await client.GetAsync("/Platform/Associations");
        using var details = await client.GetAsync($"/Platform/Associations/Details?id={fixture.SleepingId}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        foreach (var slug in new[] { "neftyanik", "summer" })
        {
            using var finance = await client.GetAsync($"/{slug}/Administration/Finance");
            Assert.Equal(HttpStatusCode.Found, finance.StatusCode);
            Assert.Contains($"/{slug}/Account/AccessDenied", finance.Headers.Location!.OriginalString);
        }
        using var inactive = await client.GetAsync("/sleeping/Administration/Finance");
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);
        using var prefixed = await client.GetAsync("/neftyanik/Platform/Associations");
        Assert.Equal(HttpStatusCode.NotFound, prefixed.StatusCode);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task ForgedRoleAndRevokedPlatformRole_CannotReadAssociations()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var forged = await fixture.ClientAsync("tenant-admin", forgePlatformClaim: true);
        using var denied = await forged.GetAsync("/Platform/Associations");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var client = await fixture.ClientAsync("operator");
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.RemoveFromRoleAsync((await users.FindByNameAsync("operator"))!, RoleNames.PlatformAdministrator)).Succeeded);
        }
        using var revoked = await client.GetAsync("/Platform/Associations");
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("member")]
    [InlineData("accountant")]
    [InlineData("tenant-admin")]
    public async Task Service_RejectsDirectCallsWithoutPlatformPermission(string? user)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        try
        {
            accessor.HttpContext = user is null ? null : await fixture.ContextAsync(scope.ServiceProvider, user);
            var reader = scope.ServiceProvider.GetRequiredService<IPlatformAssociationReader>();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetPageAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetDetailsAsync(fixture.SleepingId));
        }
        finally { accessor.HttpContext = null; }
    }

    [Fact]
    public async Task Service_ReadsWithUnresolvedContextWithoutWeakeningTenantFilters()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        try
        {
            accessor.HttpContext = await fixture.ContextAsync(scope.ServiceProvider, "operator");
            var reader = scope.ServiceProvider.GetRequiredService<IPlatformAssociationReader>();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var page = await reader.GetPageAsync();
            Assert.Equal(3, page.TotalCount);
            Assert.Equal(2, page.Items.Single(x => x.Id == fixture.SleepingId).AdministratorCount);
            Assert.NotNull(await reader.GetDetailsAsync(fixture.SleepingId));
            Assert.False(database.IsAssociationResolved);
            Assert.Empty(database.ChangeTracker.Entries<IAssociationOwned>());
            Assert.All(database.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
            Assert.Empty(await database.Members.ToListAsync());
            Assert.Empty(await database.AssociationUserMemberships.ToListAsync());
            Assert.Empty(await database.Charges.ToListAsync());
            accessor.HttpContext.Request.Path = "/neftyanik/Administration";
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetPageAsync());
            accessor.HttpContext.Request.Path = "/Platform/Associations";
            scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(await database.Associations.SingleAsync(x => x.Slug == "neftyanik"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetDetailsAsync(fixture.SleepingId));
        }
        finally { accessor.HttpContext = null; }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PortalWebApplicationFactory factory = new();
        public WebApplicationFactory<Program> App { get; private set; } = null!;
        public int SleepingId { get; private set; }
        public int SummerId { get; private set; }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.App = AuthenticationCookieTests.CreateCookieApplication(fixture.factory);
            await using (var scope = fixture.App.Services.CreateAsyncScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var sleeping = new Association { Name = "Спляче товариство", Slug = "sleeping" };
                var summer = new Association { Name = "Літній сад", Slug = "summer" };
                database.Associations.AddRange(sleeping, summer);
                await database.SaveChangesAsync();
                fixture.SleepingId = sleeping.Id;
                fixture.SummerId = summer.Id;
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                foreach (var name in new[] { "operator", "member", "accountant", "tenant-admin", "neftyanik-admin", "sleeping-admin", "blocked-admin", "former-admin" })
                {
                    var user = new ApplicationUser
                    {
                        Id = name, UserName = name, FirstName = "private-personal-value", LastName = "private-personal-value",
                        Email = name + "@private-personal-value.invalid", IsActive = name != "blocked-admin",
                        LockoutEnabled = true
                    };
                    Assert.True((await users.CreateAsync(user)).Succeeded);
                    if (name == "blocked-admin")
                    {
                        Assert.True((await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(1))).Succeeded);
                    }
                }
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                Assert.True((await roles.CreateAsync(new IdentityRole(RoleNames.PlatformAdministrator))).Succeeded);
                Assert.True((await users.AddToRoleAsync((await users.FindByNameAsync("operator"))!, RoleNames.PlatformAdministrator)).Succeeded);
            }
            foreach (var slug in new[] { "neftyanik", "sleeping" })
            {
                await using var scope = fixture.App.Services.CreateAsyncScope();
                var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var association = await database.Associations.SingleAsync(x => x.Slug == slug);
                scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(association);
                var localAdmin = slug == "neftyanik" ? "neftyanik-admin" : "sleeping-admin";
                database.AssociationUserMemberships.AddRange(
                    new AssociationUserMembership { ApplicationUserId = localAdmin, Role = RoleNames.Administrator },
                    new AssociationUserMembership { ApplicationUserId = localAdmin, Role = RoleNames.Member });
                if (slug == "neftyanik")
                {
                    database.AssociationUserMemberships.AddRange(
                        new AssociationUserMembership { ApplicationUserId = "member", Role = RoleNames.Member },
                        new AssociationUserMembership { ApplicationUserId = "accountant", Role = RoleNames.Accountant },
                        new AssociationUserMembership { ApplicationUserId = "tenant-admin", Role = RoleNames.Administrator },
                        new AssociationUserMembership { ApplicationUserId = localAdmin, Role = RoleNames.Accountant });
                }
                else
                {
                    database.AssociationUserMemberships.AddRange(
                        new AssociationUserMembership { ApplicationUserId = "blocked-admin", Role = RoleNames.Administrator },
                        new AssociationUserMembership { ApplicationUserId = "former-admin", Role = RoleNames.Administrator, IsActive = false });
                    database.Members.AddRange(
                        new Member { FullName = "private-personal-value", ApplicationUserId = localAdmin },
                        new Member { FullName = "private-personal-value" },
                        new Member { FullName = "private-personal-value", IsActive = false });
                    association.IsActive = false;
                }
                await database.SaveChangesAsync();
            }
            return fixture;
        }

        public async Task<DefaultHttpContext> ContextAsync(IServiceProvider services, string user)
        {
            var signIn = services.GetRequiredService<SignInManager<ApplicationUser>>();
            var principal = await signIn.CreateUserPrincipalAsync((await signIn.UserManager.FindByNameAsync(user))!);
            var context = new DefaultHttpContext { RequestServices = services, User = principal };
            context.Request.Path = "/Platform/Associations";
            return context;
        }

        public async Task<HttpClient> ClientAsync(string? user, bool forgePlatformClaim = false)
        {
            var cookies = new CookieContainer();
            if (user is not null)
            {
                await using var scope = App.Services.CreateAsyncScope();
                var context = await ContextAsync(scope.ServiceProvider, user);
                if (forgePlatformClaim)
                {
                    ((ClaimsIdentity)context.User.Identity!).AddClaim(new Claim(ClaimTypes.Role, RoleNames.PlatformAdministrator));
                }
                var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
                var ticket = new AuthenticationTicket(context.User, new AuthenticationProperties
                {
                    IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
                }, IdentityConstants.ApplicationScheme);
                cookies.Add(new Uri("https://localhost"), new Cookie(options.Cookie.Name!, options.TicketDataFormat.Protect(ticket), "/") { Secure = true, HttpOnly = true });
            }
            return AuthenticationCookieTests.CreateBrowser(App, cookies);
        }

        public async Task<string> SnapshotAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var snapshot = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                Associations = await database.Associations.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                Users = await database.Users.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                Memberships = await database.AssociationUserMemberships.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
                    .Select(x => new { x.Id, x.AssociationId, x.ApplicationUserId, x.Role, x.IsActive, x.CreatedAtUtc }).ToListAsync(),
                Roles = await database.UserRoles.AsNoTracking().OrderBy(x => x.UserId).ThenBy(x => x.RoleId).ToListAsync(),
                Bootstrap = await database.PlatformBootstrapStates.AsNoTracking().ToListAsync()
            });
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(snapshot));
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await factory.DisposeAsync();
        }
    }
}
