namespace Neftyanik.Portal.Domain.Constants;

public static class AssociationSlugRules
{
    public static IReadOnlyList<string> LegacyPageRoots { get; } = Array.AsReadOnly(new[]
    {
        "Account", "Administration", "Member", "Payments", "Localization", "Privacy", "Index"
    });

    public static bool IsReserved(string slug) =>
        LegacyPageRoots.Contains(slug, StringComparer.OrdinalIgnoreCase)
        || new[] { "platform", "health", "error", "home", "finance", "shared", "css", "js", "lib" }
            .Contains(slug, StringComparer.OrdinalIgnoreCase);
}
