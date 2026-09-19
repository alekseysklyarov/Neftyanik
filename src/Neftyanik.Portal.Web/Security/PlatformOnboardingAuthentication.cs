using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Web.Security;

public static class PlatformOnboardingAuthentication
{
    public const string Scheme = "DachaHub.PlatformOnboarding";
    public const string Policy = "RequirePlatformPasswordChange";
    public const string Page = "/Platform/Account/ChangeInitialPassword";
    public const string StampClaim = "dachahub:onboarding-stamp";

    public static async Task IssueAsync(HttpContext context, ApplicationUser user, SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        await context.SignOutAsync(Scheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(StampClaim, user.SecurityStamp!)
        }, Scheme));
        var clock = context.RequestServices.GetRequiredService<TimeProvider>();
        await context.SignInAsync(Scheme, principal, new AuthenticationProperties
        {
            IsPersistent = false, AllowRefresh = false,
            IssuedUtc = clock.GetUtcNow(), ExpiresUtc = clock.GetUtcNow().AddMinutes(10)
        });
    }

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = principal?.FindFirstValue(StampClaim);
        var service = context.HttpContext.RequestServices.GetRequiredService<IPlatformAdministratorOnboarding>();
        if (!PlatformAuthorization.IsPlatformRequest(context.Request)
            || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(stamp)
            || !await service.CanChangePasswordAsync(userId, stamp, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(Scheme);
        }
    }
}
