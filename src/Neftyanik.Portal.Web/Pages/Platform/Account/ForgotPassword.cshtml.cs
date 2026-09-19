using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Neftyanik.Portal.Application.Identity;

namespace Neftyanik.Portal.Web.Pages.Platform.Account;

[EnableRateLimiting("PlatformRecovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ForgotPasswordModel(IPlatformAccountRecovery recovery) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;
    public bool Submitted { get; private set; }

    public void OnGet(bool submitted = false) => Submitted = submitted;

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();
        await recovery.RequestAsync(Email, confirmEmail: false, cancellationToken);
        return RedirectToPage(new { submitted = true });
    }

    public async Task<IActionResult> OnPostConfirmationAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();
        await recovery.RequestAsync(Email, confirmEmail: true, cancellationToken);
        return RedirectToPage(new { submitted = true });
    }
}
