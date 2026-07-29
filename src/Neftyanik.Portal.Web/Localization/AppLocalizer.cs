using System.Globalization;

namespace Neftyanik.Portal.Web.Localization;

internal static class AppLocalizer
{
    public static string Get(string russian, string ukrainian, string english)
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "ru" => russian,
            "uk" => ukrainian,
            "en" => english,
            _ => ukrainian
        };
    }
}
