using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Web.Localization;
using Neftyanik.Portal.Web.Security;

namespace Neftyanik.Portal.Web.Pages.Platform.Account;

public class ChangeInitialPasswordModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager,
    IPlatformAdministratorOnboarding onboarding,
    PlatformAdministratorAccess access) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(Input.CurrentPassword)
            || string.IsNullOrWhiteSpace(Input.NewPassword)
            || Input.NewPassword != Input.ConfirmNewPassword)
        {
            return Failure();
        }
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var stamp = User.FindFirstValue(PlatformOnboardingAuthentication.StampClaim)!;
        var user = await users.FindByIdAsync(userId);
        if (user is null || !await onboarding.CanChangePasswordAsync(userId, stamp, HttpContext.RequestAborted))
        {
            await HttpContext.SignOutAsync(PlatformOnboardingAuthentication.Scheme);
            return RedirectToPage("/Platform/Account/Login");
        }
        var passwordResult = await signInManager.CheckPasswordSignInAsync(user, Input.CurrentPassword, lockoutOnFailure: true);
        if (!passwordResult.Succeeded) return Failure();
        var result = await onboarding.ChangePasswordAsync(userId, stamp, Input.CurrentPassword, Input.NewPassword, HttpContext.RequestAborted);
        if (result != PlatformPasswordChangeResult.Succeeded) return Failure();

        await HttpContext.SignOutAsync(PlatformOnboardingAuthentication.Scheme);
        await signInManager.SignOutAsync();
        user = await users.FindByIdAsync(userId);
        if (await access.IsAllowedAsync(user))
        {
            // Issue a new Identity session only after the password/flag transaction committed,
            // using the ordinary sign-in checks rather than promoting the onboarding ticket.
            var signIn = await signInManager.PasswordSignInAsync(user!, Input.NewPassword, isPersistent: false, lockoutOnFailure: true);
            if (signIn.Succeeded) return RedirectToPage("/Platform/Index");
        }
        return RedirectToPage("/Platform/Account/Login");
    }

    private PageResult Failure()
    {
        ModelState.AddModelError(string.Empty, AppLocalizer.Get(
            "Не удалось изменить пароль. Проверьте текущий пароль, подтверждение и требования к новому паролю.",
            "Не вдалося змінити пароль. Перевірте поточний пароль, підтвердження та вимоги до нового пароля.",
            "Password change failed. Check the current password, confirmation and new password requirements."));
        return Page();
    }

    public sealed class InputModel
    {
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;
        [DataType(DataType.Password)]
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }
}
