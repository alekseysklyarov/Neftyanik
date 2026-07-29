using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Web.Localization;
using Neftyanik.Portal.Web.Security;

namespace Neftyanik.Portal.Web.Pages.Account;

[Authorize]
public class ChangeInitialPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public ChangeInitialPasswordModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!user.MustChangePassword)
        {
            return RedirectToMemberDashboardOrHome();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!user.MustChangePassword)
        {
            return RedirectToMemberDashboardOrHome();
        }

        ValidateInput();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _userManager.ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            IdentityErrorLocalizer.AddErrors(ModelState, result, string.Empty, "Input.NewPassword", "Input.CurrentPassword");
            return Page();
        }

        user.MustChangePassword = false;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            IdentityErrorLocalizer.AddErrors(ModelState, updateResult, string.Empty, "Input.NewPassword");
            return Page();
        }

        await _signInManager.RefreshSignInAsync(user);
        TempData["SuccessMessage"] = AppLocalizer.Get(
            "Пароль успешно изменен.",
            "Пароль успішно змінено.",
            "The password has been changed successfully.");
        return RedirectToPage("/Member/Index");
    }

    private void ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(Input.CurrentPassword))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.CurrentPassword)}", AppLocalizer.Get(
                "Введите текущий пароль.",
                "Введіть поточний пароль.",
                "Enter the current password."));
        }

        if (string.IsNullOrWhiteSpace(Input.NewPassword))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.NewPassword)}", AppLocalizer.Get(
                "Введите новый пароль.",
                "Введіть новий пароль.",
                "Enter a new password."));
        }

        if (string.IsNullOrWhiteSpace(Input.ConfirmNewPassword))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.ConfirmNewPassword)}", AppLocalizer.Get(
                "Подтвердите новый пароль.",
                "Підтвердьте новий пароль.",
                "Confirm the new password."));
        }

        if (!string.IsNullOrWhiteSpace(Input.NewPassword)
            && !string.IsNullOrWhiteSpace(Input.ConfirmNewPassword)
            && !string.Equals(Input.NewPassword, Input.ConfirmNewPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.ConfirmNewPassword)}", AppLocalizer.Get(
                "Пароли не совпадают.",
                "Паролі не збігаються.",
                "The passwords do not match."));
        }
    }

    private IActionResult RedirectToMemberDashboardOrHome()
    {
        if (User.IsInRole(RoleNames.Member) || User.IsInRole(RoleNames.Administrator))
        {
            return RedirectToPage("/Member/Index");
        }

        return RedirectToPage("/Index");
    }

    public class InputModel
    {
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }
}
