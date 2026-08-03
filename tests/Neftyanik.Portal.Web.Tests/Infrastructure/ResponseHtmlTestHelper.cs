using System.Net;

namespace Neftyanik.Portal.Web.Tests;

internal static class ResponseHtmlTestHelper
{
    public static async Task<string> ReadDecodedHtmlAsync(this HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        return WebUtility.HtmlDecode(html);
    }
}
