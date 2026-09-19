namespace Neftyanik.Portal.Infrastructure.Identity;

public sealed class PlatformRecoveryOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public Uri GetOrigin()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
        {
            throw new InvalidOperationException("PlatformRecovery:BaseUrl must be a trusted HTTPS origin without a path, query, credentials or fragment.");
        }
        return uri;
    }
}
