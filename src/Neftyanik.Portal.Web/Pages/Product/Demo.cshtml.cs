using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Neftyanik.Portal.Web.Demo;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Product;

[AllowAnonymous]
[EnableRateLimiting("DemoEntry")]
public class DemoModel(DemoWorkspaceStore store) : PageModel
{
    public bool Expired { get; private set; }
    public void OnGet(bool expired = false)
    {
        Expired = expired;
        Response.Headers.CacheControl = "no-store";
    }

    public async Task<IActionResult> OnPostEnterAsync(string screen = "member", bool reset = false, CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (!ModelState.IsValid || PublicDemo.Screen(screen) is not { } destination) return BadRequest();
        var authentication = await HttpContext.AuthenticateAsync(PublicDemo.Scheme);
        var id = authentication.Principal?.FindFirstValue(PublicDemo.SessionClaim);
        if (reset) store.Expire(id);
        using var lease = store.Acquire(id) ?? await store.CreateAsync(cancellationToken);
        if (lease is null)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ModelState.AddModelError(string.Empty, AppLocalizer.Get("Сейчас заняты все тестовые сессии. Попробуйте немного позже.", "Зараз зайняті всі тестові сесії. Спробуйте трохи пізніше.", "All demo sessions are currently in use. Please try again shortly."));
            return Page();
        }
        await HttpContext.SignInAsync(PublicDemo.Scheme, PublicDemo.Principal(lease.Workspace.Id, destination.Role),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = lease.Workspace.ExpiresAtUtc });
        return LocalRedirect(Request.PathBase + PublicDemo.Prefix + destination.Path + "?culture=" + System.Globalization.CultureInfo.CurrentUICulture.Name);
    }
}
