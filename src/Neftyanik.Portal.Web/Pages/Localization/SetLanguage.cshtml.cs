using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Localization;

public class SetLanguageModel : PageModel
{
    public IActionResult OnGet(string? returnUrl = null)
    {
        return LocalRedirect(GetReturnUrl(returnUrl));
    }

    public IActionResult OnPost(string culture, string? returnUrl = null)
    {
        var normalizedCulture = LocalizationConfiguration.NormalizeCulture(culture);

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(normalizedCulture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                Path = "/",
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });

        return LocalRedirect(GetReturnUrl(returnUrl));
    }

    private string GetReturnUrl(string? returnUrl)
    {
        return Url.IsLocalUrl(returnUrl)
            ? returnUrl!
            : Url.Content("~/");
    }
}
