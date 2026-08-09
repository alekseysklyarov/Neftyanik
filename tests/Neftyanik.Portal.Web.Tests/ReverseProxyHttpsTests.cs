using System.Net;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class ReverseProxyHttpsTests
{
    private static readonly IReadOnlyDictionary<string, string?> ProductionReverseProxyConfiguration =
        new Dictionary<string, string?>
        {
            ["ReverseProxy:KnownProxies:0"] = "127.0.0.1",
            ["ReverseProxy:KnownProxies:1"] = "::1",
            ["ReverseProxy:KnownNetworks:0"] = "127.0.0.0/8",
            ["ReverseProxy:KnownNetworks:1"] = "::1/128"
        };

    [Fact]
    public async Task GetLogin_ForForwardedHttpsRequestInProduction_ReturnsOkWithoutRedirect()
    {
        using var factory = CreateProductionFactory();
        using var client = factory.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Account/Login");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetLogin_ForForwardedHttpsRequestInProduction_GeneratesSecureAntiforgeryCookie()
    {
        using var factory = CreateProductionFactory();
        using var client = factory.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Account/Login");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");

        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var setCookieHeaders = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToArray()
            : Array.Empty<string>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("__RequestVerificationToken", content);
        Assert.Contains(setCookieHeaders, value => value.Contains(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
        Assert.Contains(setCookieHeaders, value =>
            value.Contains(".AspNetCore.Antiforgery.", StringComparison.Ordinal)
            && value.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetLogin_ForPlainHttpRequestInProduction_RedirectsToHttps()
    {
        using var factory = CreateProductionFactory();
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(Uri.UriSchemeHttps, response.Headers.Location?.Scheme);
    }

    [Fact]
    public async Task GetLogin_InProductionWithHttpsDisabled_WorksOverHttpWithoutRedirect()
    {
        using var factory = CreateProductionFactory(new Dictionary<string, string?>
        {
            ["Security:RequireHttps"] = "false"
        });
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task GetLogin_InProductionWithHttpsDisabled_GeneratesAntiforgeryCookieWithoutSecureFlag()
    {
        using var factory = CreateProductionFactory(new Dictionary<string, string?>
        {
            ["Security:RequireHttps"] = "false"
        });
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/Account/Login");
        var content = await response.Content.ReadAsStringAsync();
        var setCookieHeaders = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToArray()
            : Array.Empty<string>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("__RequestVerificationToken", content);
        Assert.Contains(setCookieHeaders, value => value.Contains(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
        Assert.DoesNotContain(setCookieHeaders, value =>
            value.Contains(".AspNetCore.Antiforgery.", StringComparison.Ordinal)
            && value.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetLogin_InTesting_WorksOverHttp()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetLogin_InDevelopment_WorksOverHttp()
    {
        using var factory = new PortalWebApplicationFactory(environmentName: "Development", useSqlite: false);
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static PortalWebApplicationFactory CreateProductionFactory(IReadOnlyDictionary<string, string?>? additionalConfiguration = null)
    {
        var configuration = new Dictionary<string, string?>(ProductionReverseProxyConfiguration);

        if (additionalConfiguration is not null)
        {
            foreach (var entry in additionalConfiguration)
            {
                configuration[entry.Key] = entry.Value;
            }
        }

        return new PortalWebApplicationFactory(
            environmentName: "Production",
            additionalConfiguration: configuration);
    }
}
