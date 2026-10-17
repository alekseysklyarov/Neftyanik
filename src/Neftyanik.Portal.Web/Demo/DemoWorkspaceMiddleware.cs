using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Neftyanik.Portal.Application.Associations;

namespace Neftyanik.Portal.Web.Demo;

public sealed class DemoWorkspaceMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, DemoWorkspaceStore store, DemoRequestScope demo, AssociationContext association)
    {
        if (!context.Request.Path.StartsWithSegments(PublicDemo.Prefix, StringComparison.OrdinalIgnoreCase, out var remaining))
        {
            await next(context);
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        var authentication = await context.AuthenticateAsync(PublicDemo.Scheme);
        var id = authentication.Principal?.FindFirstValue(PublicDemo.SessionClaim);
        using var lease = authentication.Succeeded && PublicDemo.IsDemoUser(authentication.Principal?.FindFirstValue(ClaimTypes.NameIdentifier))
            ? store.Acquire(id) : null;
        if (lease is null)
        {
            await context.SignOutAsync(PublicDemo.Scheme);
            context.Response.Redirect(context.Request.PathBase + "/Product/Demo?expired=true");
            return;
        }
        if (remaining.StartsWithSegments("/Platform", StringComparison.OrdinalIgnoreCase)
            || remaining.StartsWithSegments("/Product", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (remaining.StartsWithSegments("/Account", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(remaining.Value, "/Account/Logout", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(context.Request.Method))
            {
                context.User = authentication.Principal!;
                if (!await context.RequestServices.GetRequiredService<IAntiforgery>().IsRequestValidAsync(context))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                await context.SignOutAsync(PublicDemo.Scheme);
            }
            context.Response.Redirect(context.Request.PathBase + "/Product/Demo");
            return;
        }
        var path = context.Request.Path;
        var pathBase = context.Request.PathBase;
        demo.Workspace = lease.Workspace;
        demo.OriginPathBase = pathBase;
        association.Resolve(lease.Workspace.Association);
        context.Request.PathBase = pathBase + PublicDemo.Prefix;
        context.Request.Path = remaining.HasValue ? remaining : "/";
        try { await next(context); }
        finally { context.Request.Path = path; context.Request.PathBase = pathBase; }
    }
}
