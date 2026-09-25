using Microsoft.AspNetCore.Authorization;
using Neftyanik.Portal.Application.Associations;

namespace Neftyanik.Portal.Web.Security;

public sealed class PlatformAssociationReadAccess(
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorization) : IPlatformAssociationReadAccess, IPlatformAssociationWriteAccess
{
    public async Task<string> GetOperatorIdAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAllowedAsync(cancellationToken);
        return httpContextAccessor.HttpContext!.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("A platform operator is required.");
    }

    public async Task EnsureAllowedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = httpContextAccessor.HttpContext;
        if (context is null || !(await authorization.AuthorizeAsync(
                context.User, null, PlatformAuthorization.PolicyName)).Succeeded)
        {
            throw new UnauthorizedAccessException("Platform administrator access is required.");
        }
    }
}
