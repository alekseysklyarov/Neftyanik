using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
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

        // The query provider has priority over the culture cookie. Remove the previous
        // culture from the return URL so the newly selected language can take effect.
        var target = GetReturnUrl(returnUrl);
        var fragmentIndex = target.IndexOf('#');
        var fragment = fragmentIndex >= 0 ? target[fragmentIndex..] : string.Empty;
        var withoutFragment = fragmentIndex >= 0 ? target[..fragmentIndex] : target;
        var queryIndex = withoutFragment.IndexOf('?');
        if (queryIndex >= 0)
        {
            var query = QueryHelpers.ParseQuery(withoutFragment[(queryIndex + 1)..]);
            target = withoutFragment[..queryIndex] + QueryString.Create(query.Where(item =>
                !string.Equals(item.Key, "culture", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item.Key, "ui-culture", StringComparison.OrdinalIgnoreCase))) + fragment;
        }
        return LocalRedirect(target);
    }

    private string GetReturnUrl(string? returnUrl)
    {
        return Url.IsLocalUrl(returnUrl)
            && Neftyanik.Portal.Web.Associations.TenantReturnUrls.IsWithinAssociation(Request, returnUrl)
            ? returnUrl!
            : Url.Content("~/");
    }
}
