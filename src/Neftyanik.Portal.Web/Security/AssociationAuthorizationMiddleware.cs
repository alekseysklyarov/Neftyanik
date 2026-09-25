using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Security;

public sealed class AssociationAuthorizationMiddleware(RequestDelegate next)
{
    public const string RoleClaimType = "dachahub:association-role";

    public async Task InvokeAsync(HttpContext context, IAssociationMembershipService memberships, ApplicationDbContext database)
    {
        // Never mutate the authentication ticket: it is global and may be renewed by Identity.
        var identities = context.User.Identities.Select(identity => new ClaimsIdentity(
            identity.Claims.Where(claim => claim.Type != identity.RoleClaimType && claim.Type != RoleClaimType),
            identity.AuthenticationType, identity.NameClaimType, RoleClaimType)).ToList();
        var principal = new ClaimsPrincipal(identities);
        var authenticatedIdentity = identities.FirstOrDefault(x => x.IsAuthenticated);
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (authenticatedIdentity is not null && userId is not null)
        {
            var roles = await memberships.GetRolesAsync(userId, context.RequestAborted);
            authenticatedIdentity.AddClaims(roles.Select(role => new Claim(RoleClaimType, role)));
        }
        context.User = principal;
        if (authenticatedIdentity is not null && userId is not null && database.IsAssociationResolved
            && await database.Users.AsNoTracking().AnyAsync(x => x.Id == userId && x.MustChangePassword, context.RequestAborted))
        {
            var path = context.Request.Path.Value?.TrimEnd('/');
            if (!string.Equals(path, "/Account/ChangeInitialPassword", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, "/Account/Logout", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Redirect(context.Request.PathBase + "/Account/ChangeInitialPassword");
                return;
            }
        }
        await next(context);
    }
}
