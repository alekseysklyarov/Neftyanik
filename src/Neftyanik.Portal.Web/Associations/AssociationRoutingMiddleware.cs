using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Associations;

public sealed class AssociationRoutingMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly HashSet<string> LegacyPageRoots = new(StringComparer.Ordinal)
    {
        "Account", "Administration", "Member", "Payments", "Localization", "Privacy", "Index"
    };

    public AssociationRoutingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext httpContext, ApplicationDbContext database, AssociationContext associationContext, IWebHostEnvironment environment)
    {
        var path = httpContext.Request.Path;
        if (path == "/health" || path == "/Error" || path.StartsWithSegments("/Platform", StringComparison.OrdinalIgnoreCase))
        {
            await _next(httpContext);
            return;
        }
        var segments = (path.Value ?? string.Empty).Split('/', 3, StringSplitOptions.None);
        var slug = segments.Length > 1 ? segments[1] : string.Empty;
        if (slug.Length == 0 || LegacyPageRoots.Contains(slug))
        {
            var suffix = slug.Length == 0 ? "/" : path.Value;
            httpContext.Response.Redirect(httpContext.Request.PathBase + "/neftyanik" + suffix + httpContext.Request.QueryString,
                permanent: false, preserveMethod: true);
            return;
        }

        // Public asset roots and filenames are not tenant slugs, even when the file is missing.
        if (System.IO.Path.HasExtension(slug) || environment.WebRootFileProvider.GetDirectoryContents(slug).Exists)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Compare in SQL using the same collation as the globally unique Slug index.
        var association = await database.Associations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Slug == slug && x.IsActive, httpContext.RequestAborted);
        if (association is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!string.Equals(slug, association.Slug, StringComparison.Ordinal) || segments.Length == 2)
        {
            var suffix = segments.Length == 3 ? "/" + segments[2] : "/";
            httpContext.Response.Redirect(httpContext.Request.PathBase + "/" + association.Slug + suffix + httpContext.Request.QueryString,
                permanent: false, preserveMethod: true);
            return;
        }

        if (new PathString("/" + segments[2]).StartsWithSegments("/Platform", StringComparison.OrdinalIgnoreCase))
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        associationContext.Resolve(association);
        var originalPathBase = httpContext.Request.PathBase;
        httpContext.Request.PathBase = originalPathBase.Add(new PathString("/" + association.Slug));
        httpContext.Request.Path = new PathString("/" + segments[2]);
        try
        {
            await _next(httpContext);
        }
        catch (AssociationIsolationException)
        {
            if (httpContext.Response.HasStarted)
            {
                throw;
            }
            httpContext.Response.Clear();
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        }
        finally
        {
            httpContext.Request.PathBase = originalPathBase;
            httpContext.Request.Path = path;
        }
    }
}
