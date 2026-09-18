using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Security;

internal static class LegacyAuthenticationCookieCleanup
{
    public static async Task DeleteAsync(HttpContext context, CookieAuthenticationOptions options)
    {
        var database = context.RequestServices.GetRequiredService<ApplicationDbContext>();
        var slugs = await database.Associations.AsNoTracking()
            .Select(association => association.Slug)
            .ToListAsync(context.RequestAborted);

        // Stage 2 issued this same cookie at /{slug}. Clear even inactive/other tenants:
        // their cookies are not sent on this request, but could otherwise restore a session later.
        // Run after Identity's root deletion, which can remove pending same-name Set-Cookie headers.
        context.Response.OnStarting(() =>
        {
            foreach (var slug in slugs.OrderBy(slug => slug.Length))
            {
                var cookieOptions = options.Cookie.Build(context);
                cookieOptions.Path = "/" + slug;
                options.CookieManager.DeleteCookie(context, options.Cookie.Name!, cookieOptions);
            }

            return Task.CompletedTask;
        });
    }
}
