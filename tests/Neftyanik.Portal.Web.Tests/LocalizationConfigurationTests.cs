using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Neftyanik.Portal.Web.Localization;
using Neftyanik.Portal.Web.Pages.Localization;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class LocalizationConfigurationTests
{
    [Fact]
    public void CreateOptions_ConfiguresDefaultAndSupportedCultures()
    {
        var options = LocalizationConfiguration.CreateOptions();
        var supportedCultures = new[] { "uk-UA", "ru-RU", "en-US" };

        Assert.Equal(LocalizationConfiguration.DefaultCultureName, options.DefaultRequestCulture.Culture.Name);
        Assert.Equal(supportedCultures, options.SupportedCultures!.Select(culture => culture.Name).ToArray());
        Assert.Equal(supportedCultures, options.SupportedUICultures!.Select(culture => culture.Name).ToArray());
        Assert.True(options.ApplyCurrentCultureToResponseHeaders);
    }

    [Fact]
    public void OnPost_SetsCookieForRequestedCultureAndRedirectsToLocalUrl()
    {
        var model = CreateModel();

        var result = model.OnPost("uk-UA", "/Member/Index");

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Member/Index", redirect.Url);
        Assert.Contains("c=uk-UA|uic=uk-UA", Uri.UnescapeDataString(model.Response.Headers.SetCookie.ToString()));
        Assert.Contains("path=/", model.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnPost_FallsBackToDefaultCultureAndRootForUnsupportedOrExternalValues()
    {
        var model = CreateModel();

        var result = model.OnPost("de-DE", "https://example.com/external");

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/", redirect.Url);
        Assert.Contains(
            $"c={LocalizationConfiguration.DefaultCultureName}|uic={LocalizationConfiguration.DefaultCultureName}",
            Uri.UnescapeDataString(model.Response.Headers.SetCookie.ToString()));
    }

    [Theory]
    [InlineData("/Product/Try/Administration/Finance?culture=uk-UA&ui-culture=uk-UA&year=2026#totals", "/Product/Try/Administration/Finance?year=2026#totals")]
    [InlineData("/Member?Culture=uk-UA&UI-Culture=uk-UA", "/Member")]
    [InlineData("/Member?culture=uk-UA&filter=a&filter=b", "/Member?filter=a&filter=b")]
    public void OnPost_RemovesPreviousQueryCultureAndPreservesOtherParameters(string returnUrl, string expected)
    {
        var model = CreateModel();

        var redirect = Assert.IsType<LocalRedirectResult>(model.OnPost("ru-RU", returnUrl));

        Assert.Equal(expected, redirect.Url);
        Assert.Contains("c=ru-RU|uic=ru-RU", Uri.UnescapeDataString(model.Response.Headers.SetCookie.ToString()));
    }

    private static SetLanguageModel CreateModel()
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var pageContext = new PageContext(actionContext);

        return new SetLanguageModel
        {
            PageContext = pageContext,
            Url = new UrlHelper(actionContext)
        };
    }
}
