using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Web.Security;

public static class PlatformAuthorization
{
    public const string PolicyName = "RequirePlatformAdministrator";

    public static bool IsPlatformRequest(HttpRequest request) =>
        request.Path.StartsWithSegments("/Platform", StringComparison.OrdinalIgnoreCase);
}

public sealed class PlatformAdministratorAccess(UserManager<ApplicationUser> users)
{
    public async Task<bool> IsAllowedAsync(ApplicationUser? user)
    {
        return user is { IsActive: true, MustChangePassword: false }
            && !await users.IsLockedOutAsync(user)
            && await users.IsInRoleAsync(user, RoleNames.PlatformAdministrator);
    }
}

public sealed class PlatformAdministratorRequirement : IAuthorizationRequirement
{
}

public sealed class PlatformAdministratorHandler(
    UserManager<ApplicationUser> users,
    PlatformAdministratorAccess access,
    IAssociationContext associationContext,
    IHttpContextAccessor httpContextAccessor) : AuthorizationHandler<PlatformAdministratorRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PlatformAdministratorRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || associationContext.IsResolved
            || httpContextAccessor.HttpContext is not { } httpContext
            || !PlatformAuthorization.IsPlatformRequest(httpContext.Request))
        {
            return;
        }

        var user = await users.GetUserAsync(context.User);
        if (await access.IsAllowedAsync(user))
        {
            context.Succeed(requirement);
        }
    }
}
