namespace Neftyanik.Portal.Web.Associations;

internal static class TenantReturnUrls
{
    public static bool IsWithinAssociation(HttpRequest request, string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl.Contains('\\'))
        {
            return false;
        }
        var path = returnUrl.Split('?', '#')[0];
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }
        var decodedPath = Uri.UnescapeDataString(path);
        if (decodedPath.Contains('\\') || decodedPath.Split('/').Any(segment => segment is "." or ".."))
        {
            return false;
        }
        return !request.PathBase.HasValue || new PathString(decodedPath).StartsWithSegments(request.PathBase);
    }
}
