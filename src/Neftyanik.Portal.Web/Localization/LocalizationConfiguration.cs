using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace Neftyanik.Portal.Web.Localization;

public static class LocalizationConfiguration
{
    public const string DefaultCultureName = "uk-UA";

    public static readonly IReadOnlyList<CultureInfo> SupportedCultures =
    [
        new CultureInfo(DefaultCultureName),
        new CultureInfo("ru-RU"),
        new CultureInfo("en-US")
    ];

    public static RequestLocalizationOptions CreateOptions()
    {
        var supportedCultures = SupportedCultures.ToList();

        return new RequestLocalizationOptions
        {
            ApplyCurrentCultureToResponseHeaders = true,
            DefaultRequestCulture = new RequestCulture(DefaultCultureName),
            SupportedCultures = supportedCultures,
            SupportedUICultures = supportedCultures
        };
    }

    public static string NormalizeCulture(string? cultureName)
    {
        return SupportedCultures.Any(culture => string.Equals(culture.Name, cultureName, StringComparison.OrdinalIgnoreCase))
            ? cultureName!
            : DefaultCultureName;
    }
}
