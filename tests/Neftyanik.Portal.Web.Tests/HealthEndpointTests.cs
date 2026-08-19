using System.Net;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", content);
    }

    [Fact]
    public async Task GetSiteCss_ReturnsPortalHomeStyles()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/css/site.css");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(".portal-hero", content);
        Assert.Contains(".portal-feature-icon svg", content);
        Assert.Contains("--portal-bg: #f6f8fb;", content);
        Assert.Contains(".member-dashboard-tab-list", content);
    }
}
