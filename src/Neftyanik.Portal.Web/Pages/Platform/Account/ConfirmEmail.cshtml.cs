using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Neftyanik.Portal.Application.Identity;

namespace Neftyanik.Portal.Web.Pages.Platform.Account;

[EnableRateLimiting("PlatformRecovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ConfirmEmailModel(IPlatformAccountRecovery recovery) : PageModel
{
    [BindProperty, Required, StringLength(450)]
    public string UserId { get; set; } = string.Empty;
    [BindProperty, Required, StringLength(4096)]
    public string Token { get; set; } = string.Empty;

    public void OnGet() => Response.Headers["Referrer-Policy"] = "no-referrer";

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (ModelState.IsValid && await recovery.ConfirmEmailAsync(UserId, Token, cancellationToken))
            return RedirectToPage("/Platform/Account/ForgotPassword");
        ModelState.AddModelError(string.Empty, "Ссылка недействительна. Запросите новое письмо подтверждения.");
        return Page();
    }
}
