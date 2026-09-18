using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class TenantHttpIsolationTests
{
    private const string UserId = "stage2-global-user";
    private static readonly TestAuthenticatedUser Administrator = new(UserId, RoleNames.Administrator, RoleNames.Accountant, RoleNames.Member);

    [Theory]
    [InlineData("neftyanik")]
    [InlineData("second")]
    public async Task ListsDetailsAndFinance_ReturnOnlyUrlTenantDataEvenWithGlobalRoles(string slug)
    {
        using var factory = new PortalWebApplicationFactory();
        var data = await SeedAsync(factory);
        var own = data[slug];
        var foreignSlug = slug == "neftyanik" ? "second" : "neftyanik";
        using var client = factory.CreateAuthenticatedClient(Administrator);
        foreach (var path in new[]
        {
            "/Administration/Members", "/Administration/Plots",
            $"/Administration/Plots/Details/{own.PlotId}",
            $"/Administration/Members/Finance/{own.MemberId}/Finance", "/Member"
        })
        {
            using var response = await client.GetAsync($"/{slug}{path}");
            var html = await response.ReadDecodedHtmlAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains($"member-marker-{slug}", html);
            Assert.DoesNotContain($"member-marker-{foreignSlug}", html);
            Assert.DoesNotContain($"charge-marker-{foreignSlug}", html);
        }
    }

    [Theory]
    [InlineData("neftyanik")]
    [InlineData("second")]
    public async Task ForeignIds_AreNotFoundOnDetailsAndMemberFinance(string slug)
    {
        using var factory = new PortalWebApplicationFactory();
        var data = await SeedAsync(factory);
        var foreign = data[slug == "neftyanik" ? "second" : "neftyanik"];
        using var client = factory.CreateAuthenticatedClient(Administrator);
        foreach (var path in new[]
        {
            $"/Administration/Plots/Details/{foreign.PlotId}",
            $"/Administration/Plots/Edit/{foreign.PlotId}",
            $"/Administration/Members/Details/{foreign.MemberId}",
            $"/Administration/Members/Finance/{foreign.MemberId}/Finance"
        })
        {
            using var response = await client.GetAsync($"/{slug}{path}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("member-marker-", await response.ReadDecodedHtmlAsync());
        }
    }

    [Theory]
    [InlineData("neftyanik")]
    [InlineData("second")]
    public async Task Create_IgnoresForgedTenantFieldsAndAssignsUrlTenant(string slug)
    {
        using var factory = new PortalWebApplicationFactory();
        var data = await SeedAsync(factory);
        var other = data[slug == "neftyanik" ? "second" : "neftyanik"];
        using var client = factory.CreateAuthenticatedClient(Administrator);
        var token = await TokenAsync(client, $"/{slug}/Administration/Plots/Create");
        using var response = await client.PostAsync($"/{slug}/Administration/Plots/Create?AssociationId={other.AssociationId}", Form(token,
            ("Input.Number", "20"), ("Input.IsActive", "true"),
            ("AssociationId", other.AssociationId.ToString()), ("Input.AssociationId", other.AssociationId.ToString()),
            ("Association.Id", other.AssociationId.ToString()), ("associationSlug", other.Slug)));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal($"/{slug}/Administration/Plots", response.Headers.Location?.OriginalString);
        await factory.ExecuteDbContextAsync(async context =>
        {
            var created = await context.Plots.AsNoTracking().SingleAsync(x => x.Number == "20");
            Assert.Equal(data[slug].AssociationId, created.AssociationId);
        }, slug);
        await factory.ExecuteDbContextAsync(async context => Assert.False(await context.Plots.AnyAsync(x => x.Number == "20")), other.Slug);
    }

    [Theory]
    [InlineData("neftyanik", "Edit")]
    [InlineData("second", "Edit")]
    [InlineData("neftyanik", "Archive")]
    [InlineData("second", "Archive")]
    public async Task ForgedMutationOfForeignPlot_IsNotFoundAndLeavesStoredRecordUnchanged(string slug, string operation)
    {
        using var factory = new PortalWebApplicationFactory();
        var data = await SeedAsync(factory);
        var other = data[slug == "neftyanik" ? "second" : "neftyanik"];
        using var client = factory.CreateAuthenticatedClient(Administrator);
        var token = await TokenAsync(client, $"/{slug}/Administration/Plots/Create");
        using var response = await client.PostAsync($"/{slug}/Administration/Plots/{operation}/{other.PlotId}", Form(token,
            ("id", other.PlotId.ToString()), ("Input.Number", "stolen"), ("Input.IsActive", "false"),
            ("AssociationId", data[slug].AssociationId.ToString()), ("Input.AssociationId", data[slug].AssociationId.ToString())));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await factory.ExecuteDbContextAsync(async context =>
        {
            var original = await context.Plots.AsNoTracking().SingleAsync(x => x.Id == other.PlotId);
            Assert.Equal("10", original.Number);
            Assert.True(original.IsActive);
            Assert.Equal(other.AssociationId, original.AssociationId);
        }, other.Slug);
    }

    [Theory]
    [InlineData("neftyanik")]
    [InlineData("second")]
    public async Task ForeignChargeCancellation_IsDeniedAndPreservesFinancialRecord(string slug)
    {
        using var factory = new PortalWebApplicationFactory();
        var data = await SeedAsync(factory);
        var own = data[slug];
        var other = data[slug == "neftyanik" ? "second" : "neftyanik"];
        using var client = factory.CreateAuthenticatedClient(Administrator);
        var token = await TokenAsync(client, $"/{slug}/Administration/Members/Finance/{own.MemberId}/Charges/{own.ChargeId}/Cancel");
        foreach (var memberId in new[] { own.MemberId, other.MemberId })
        {
            var path = $"/{slug}/Administration/Members/Finance/{memberId}/Charges/{other.ChargeId}/Cancel";
            using var get = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            using var post = await client.PostAsync(path, Form(token, ("Input.CancellationReason", "forged cancellation")));
            Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        }
        await factory.ExecuteDbContextAsync(async context =>
        {
            var unchanged = await context.Charges.AsNoTracking().SingleAsync(x => x.Id == other.ChargeId);
            Assert.Null(unchanged.CancelledAtUtc);
            Assert.Null(unchanged.CancellationReason);
            Assert.Equal(other.AssociationId, unchanged.AssociationId);
        }, other.Slug);
    }

    [Fact]
    public async Task SqlServerSlugComparison_UsesDatabaseCollationAndRedirectsToCanonicalCase()
    {
        using var factory = new PortalWebApplicationFactory(environmentName: "Development", useSqlite: false);
        await SeedAsync(factory);
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync("/NEFTYANIK/Administration/Members?search=test");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("/neftyanik/Administration/Members?search=test", response.Headers.Location?.OriginalString);
        using var inactive = await client.GetAsync("/INACTIVE/");
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);
        Assert.Null(inactive.Headers.Location);
    }

    [Theory]
    [InlineData("/missing/Administration/Members")]
    [InlineData("/inactive/Administration/Members")]
    [InlineData("/inactive/css/site.css")]
    public async Task UnknownOrInactiveTenant_IsNotFoundWithoutFallback(string path)
    {
        using var factory = new PortalWebApplicationFactory();
        await SeedAsync(factory);
        using var client = factory.CreateAuthenticatedClient(Administrator);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("member-marker-", await response.ReadDecodedHtmlAsync());
    }

    [Theory]
    [InlineData("/", "/neftyanik/")]
    [InlineData("/Administration/Members?search=test", "/neftyanik/Administration/Members?search=test")]
    [InlineData("/neftyanik", "/neftyanik/")]
    [InlineData("/second", "/second/")]
    public async Task RootLegacyAndTenantRoot_RedirectExplicitlyToCanonicalUrl(string path, string expected)
    {
        using var factory = new PortalWebApplicationFactory();
        await SeedAsync(factory);
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/neftyanik/css/site.css", "text/css")]
    [InlineData("/neftyanik/js/site.js", "javascript")]
    [InlineData("/neftyanik/lib/bootstrap/dist/css/bootstrap.min.css", "text/css")]
    [InlineData("/neftyanik/lib/bootstrap/dist/js/bootstrap.bundle.min.js", "javascript")]
    [InlineData("/neftyanik/lib/jquery/dist/jquery.min.js", "javascript")]
    [InlineData("/second/css/site.css", "text/css")]
    [InlineData("/css/site.css", "text/css")]
    public async Task StaticAssets_AreServedAtTenantAndPublicPaths(string path, string mediaType)
    {
        using var factory = new PortalWebApplicationFactory();
        await SeedAsync(factory);
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(mediaType, response.Content.Headers.ContentType!.MediaType!);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("/css/not-a-file.css")]
    [InlineData("/favicon-missing.ico")]
    public async Task MissingPublicAssets_AreNotTreatedAsTenantPages(string path)
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("neftyanik")]
    [InlineData("second")]
    public async Task NavigationFormsAndPagination_KeepCurrentTenantPrefix(string slug)
    {
        using var factory = new PortalWebApplicationFactory();
        await SeedAsync(factory);
        await factory.ExecuteDbContextAsync(async context =>
        {
            context.Members.AddRange(Enumerable.Range(1, 60).Select(index => new Member { FullName = $"pagination-member-{index}" }));
            await context.SaveChangesAsync();
        }, slug);
        using var client = factory.CreateAuthenticatedClient(Administrator);
        using var response = await client.GetAsync($"/{slug}/Administration/Members");
        var html = await response.ReadDecodedHtmlAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = Regex.Matches(html, "(?:href|action|src)=\"(?<path>/[^\"]*)\"").Select(match => match.Groups["path"].Value).ToArray();
        Assert.NotEmpty(paths);
        Assert.All(paths, path => Assert.True(path == $"/{slug}" || path.StartsWith($"/{slug}/", StringComparison.Ordinal), path));
        Assert.Contains(paths, path => path.Contains("Localization/SetLanguage", StringComparison.Ordinal));
        Assert.Contains(paths, path => path.Contains("Account/Logout", StringComparison.Ordinal));
        Assert.Contains(paths, path => path.Contains('?', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/second/Administration/Members")]
    [InlineData("//example.com/")]
    [InlineData("/neftyanik/../second/")]
    [InlineData("/neftyanik/%2e%2e/second/")]
    public async Task LanguageSwitch_RejectsReturnUrlsOutsideCurrentTenant(string returnUrl)
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();
        var token = await TokenAsync(client, "/neftyanik/Account/Login");
        using var response = await client.PostAsync("/neftyanik/Localization/SetLanguage", Form(token, ("culture", "en-US"), ("returnUrl", returnUrl)));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/neftyanik/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Login_RejectsCrossTenantReturnUrlAndLogoutStaysWithinTenant()
    {
        using var factory = new PortalWebApplicationFactory();
        await SeedAsync(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var result = await manager.AddPasswordAsync((await manager.FindByIdAsync(UserId))!, "Pass123!");
            Assert.True(result.Succeeded);
        }
        using var client = factory.CreateAnonymousClient();
        var token = await TokenAsync(client, "/neftyanik/Account/Login");
        using var login = await client.PostAsync("/neftyanik/Account/Login", Form(token,
            ("Input.Login", "stage2-user"), ("Input.Password", "Pass123!"), ("ReturnUrl", "/second/Administration/Members")));
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Assert.Equal("/neftyanik/Administration", login.Headers.Location?.OriginalString);

        using var authenticated = factory.CreateAuthenticatedClient(Administrator);
        token = await TokenAsync(authenticated, "/second/Administration/Plots/Create");
        using var logout = await authenticated.PostAsync("/second/Account/Logout", Form(token));
        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);
        Assert.Equal("/second", logout.Headers.Location?.OriginalString);
    }

    private static async Task<Dictionary<string, TenantData>> SeedAsync(PortalWebApplicationFactory factory)
    {
        await factory.ExecuteDbContextAsync(async context =>
        {
            context.Associations.AddRange(new Association { Name = "Second", Slug = "second" }, new Association { Name = "Inactive", Slug = "inactive", IsActive = false });
            context.Users.Add(new ApplicationUser { Id = UserId, UserName = "stage2-user", NormalizedUserName = "STAGE2-USER", FirstName = "Global", LastName = "User" });
            await context.SaveChangesAsync();
        });
        var result = new Dictionary<string, TenantData>();
        foreach (var slug in new[] { "neftyanik", "second" })
        {
            await factory.ExecuteDbContextAsync(async context =>
            {
                context.AssociationUserMemberships.AddRange(Administrator.Roles.Select(role => new AssociationUserMembership { ApplicationUserId = UserId, Role = role }));
                var member = new Member { FullName = $"member-marker-{slug}", ApplicationUserId = UserId };
                var plot = new Plot { Number = "10", Address = $"plot-marker-{slug}" };
                var chargeType = new ChargeType { Name = "Test charge", Code = "TEST" };
                var charge = new Charge { Plot = plot, ChargeType = chargeType, Amount = slug == "neftyanik" ? 100m : 350m, ChargeDate = new DateOnly(2026, 1, 1), Description = $"charge-marker-{slug}" };
                context.AddRange(member, plot, new PlotOwnership { Member = member, Plot = plot }, charge);
                await context.SaveChangesAsync();
                result[slug] = new TenantData(slug, plot.AssociationId, plot.Id, member.Id, charge.Id);
            }, slug);
        }
        return result;
    }

    private static async Task<string> TokenAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var match = Regex.Match(await response.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"");
        Assert.True(match.Success, $"Missing antiforgery token at {url}");
        return WebUtility.HtmlDecode(match.Groups["token"].Value);
    }

    private static FormUrlEncodedContent Form(string token, params (string Name, string Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string>(value.Name, value.Value))
            .Append(new KeyValuePair<string, string>("__RequestVerificationToken", token)));

    private sealed record TenantData(string Slug, int AssociationId, int PlotId, int MemberId, long ChargeId);
}
