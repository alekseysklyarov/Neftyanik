using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Web.Security;

namespace Neftyanik.Portal.Web.Pages.Platform.Account;

[EnableRateLimiting("PlatformRecovery")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ResetPasswordModel(IPlatformAccountRecovery recovery) : PageModel
{
    [BindProperty, Required, StringLength(450)]
    public string UserId { get; set; } = string.Empty;
    [BindProperty, Required, StringLength(4096)]
    public string Token { get; set; } = string.Empty;
    [BindProperty, Required, DataType(DataType.Password), StringLength(128, MinimumLength = 6)]
    public string NewPassword { get; set; } = string.Empty;
    [BindProperty, Compare(nameof(NewPassword)), DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;

    public void OnGet() => Response.Headers["Referrer-Policy"] = "no-referrer";

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (ModelState.IsValid && await recovery.ResetPasswordAsync(UserId, Token, NewPassword, cancellationToken))
        {
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            await HttpContext.SignOutAsync(PlatformOnboardingAuthentication.Scheme);
            return RedirectToPage("/Platform/Account/Login");
        }
        ModelState.AddModelError(string.Empty, "Не удалось изменить пароль. Проверьте требования к паролю или запросите новую ссылку.");
        return Page();
    }
}
