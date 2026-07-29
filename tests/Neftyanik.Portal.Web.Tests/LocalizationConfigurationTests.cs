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

        Assert.Equal(LocalizationConfiguration.DefaultCultureName, options.DefaultRequestCulture.Culture.Name);
        Assert.Equal(
            ["uk-UA", "ru-RU", "en-US"],
            options.SupportedCultures!.Select(culture => culture.Name).ToArray());
        Assert.Equal(
            ["uk-UA", "ru-RU", "en-US"],
            options.SupportedUICultures!.Select(culture => culture.Name).ToArray());
        Assert.True(options.ApplyCurrentCultureToResponseHeaders);
    }

    [Fact]
    public void OnPost_SetsCookieForRequestedCultureAndRedirectsToLocalUrl()
    {
        var model = CreateModel();

        var result = model.OnPost("uk-UA", "/Member/Index");

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Member/Index", redirect.Url);
        Assert.Contains("c=uk-UA|uic=uk-UA", model.Response.Headers.SetCookie.ToString());
        Assert.Contains("path=/", model.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnPost_FallsBackToDefaultCultureAndRootForUnsupportedOrExternalValues()
    {
        var model = CreateModel();

        var result = model.OnPost("de-DE", "https://example.com/external");

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("~/", redirect.Url);
        Assert.Contains($"c={LocalizationConfiguration.DefaultCultureName}|uic={LocalizationConfiguration.DefaultCultureName}", model.Response.Headers.SetCookie.ToString());
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
