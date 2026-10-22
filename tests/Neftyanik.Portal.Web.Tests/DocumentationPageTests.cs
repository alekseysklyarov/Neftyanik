using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Web.Demo;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public sealed class DocumentationPageTests
{
    private const string Path = "/Product/Documentation";

    [Theory]
    [InlineData("ru-RU", "Как работать с DachaHub", "Импорт и экспорт платежей за год")]
    [InlineData("uk-UA", "Як працювати з DachaHub", "Імпорт та експорт платежів за рік")]
    [InlineData("en-US", "How to use DachaHub", "Import and export annual payments")]
    public async Task PublicGuideWorksWithoutLoginAndContainsLocalizedTaskInstructions(string culture, string title, string excelTitle)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Members.Add(new Member { FullName = "PRIVATE-MEMBER-DO-NOT-PUBLISH", PhoneNumber = "+380501111111" }); await db.SaveChangesAsync();
        });
        using var client = factory.CreateAnonymousClient(cultureName: culture);
        using var response = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.ReadDecodedHtmlAsync();
        Assert.Contains(title, html); Assert.Contains(excelTitle, html);
        Assert.Equal(18, Regex.Matches(html, "data-guide-article(?: |>)").Count);
        Assert.Contains("id=\"guide-search\"", html); Assert.Contains("id=\"guide-role\"", html);
        Assert.Contains("Key", html); Assert.Contains("SecondNight", html); Assert.Contains("10000", html);
        Assert.DoesNotContain("PRIVATE-MEMBER-DO-NOT-PUBLISH", html); Assert.DoesNotContain("+380501111111", html);
        var articleIds = Regex.Matches(html, "<article[^>]*id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Equal(18, articleIds.Count);
        foreach (var anchor in Regex.Matches(html, "href=\"#([^\"]+)\"").Select(m => m.Groups[1].Value))
            Assert.True(anchor is "guide-top" or "main" || articleIds.Contains(anchor), "Missing anchor: " + anchor);
        foreach (var asset in new[] { "/css/documentation.css", "/js/documentation.js" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(asset)).StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Product/Management")]
    [InlineData("/Product/Demo")]
    public async Task PublicPagesLinkToTheGuide(string page)
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient(cultureName: "ru-RU");
        var html = await client.GetStringAsync(page);
        Assert.Contains("href=\"/Product/Documentation?culture=ru-RU\"", html);
        if (page == "/") Assert.True(Regex.Matches(html, "/Product/Documentation\\?culture=ru-RU").Count >= 3);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Path + "?culture=ru-RU")).StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.Administrator)]
    [InlineData(RoleNames.Accountant)]
    [InlineData(RoleNames.Member)]
    public async Task PortalLinksOpenThePublicGuideInsteadOfATenantProductRoute(string role)
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("documentation-" + role, role), cultureName: "en-US");
        using var response = await client.GetAsync("/neftyanik/Privacy"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("href=\"/Product/Documentation?culture=en-US\"", html);
        Assert.DoesNotContain("/neftyanik/Product/Documentation", html);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Path + "?culture=en-US")).StatusCode);
    }

    [Fact]
    public async Task LinksPreserveHostingPathWithoutTheAssociationPrefix()
    {
        using var factory = new PortalWebApplicationFactory();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, HostingPathFilter>()));
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var home = await client.GetStringAsync("/host/neftyanik/?culture=en-US");
        Assert.Contains("href=\"/host/Product/Documentation?culture=en-US\"", home);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/host/Product/Documentation?culture=en-US")).StatusCode);
    }

    [Fact]
    public async Task DemoHeaderLinksOutToPublicDocumentation()
    {
        using var factory = new PortalWebApplicationFactory();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            PublicDemo.UseScopedDatabase(services);
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = PublicDemo.SelectorScheme;
                options.DefaultChallengeScheme = PublicDemo.SelectorScheme;
                options.DefaultForbidScheme = PublicDemo.SelectorScheme;
            });
        }));
        using var client = AuthenticationCookieTests.CreateBrowser(app, new CookieContainer());
        var csrf = await AuthenticationCookieTests.TokenAsync(client, "/Product/Demo?culture=en-US");
        using var enter = await client.PostAsync("/Product/Demo?handler=Enter&culture=en-US", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["screen"] = "members", ["__RequestVerificationToken"] = csrf }));
        Assert.Equal(HttpStatusCode.Redirect, enter.StatusCode);
        var html = await client.GetStringAsync(enter.Headers.Location!);
        Assert.Contains("href=\"/Product/Documentation?culture=en-US\"", html);
        Assert.DoesNotContain("/Product/Try/Product/Documentation", html);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Path + "?culture=en-US")).StatusCode);
    }

    private sealed class HostingPathFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => { app.UsePathBase("/host"); next(app); };
    }
}
