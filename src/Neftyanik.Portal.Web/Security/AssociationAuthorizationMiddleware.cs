using System.Security.Claims;
using Neftyanik.Portal.Application.Associations;

namespace Neftyanik.Portal.Web.Security;

public sealed class AssociationAuthorizationMiddleware(RequestDelegate next)
{
    public const string RoleClaimType = "dachahub:association-role";

    public async Task InvokeAsync(HttpContext context, IAssociationMembershipService memberships)
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
        await next(context);
    }
}
