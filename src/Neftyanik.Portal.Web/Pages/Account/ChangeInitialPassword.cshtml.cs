using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
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
        TempData["SuccessMessage"] = "Пароль успешно изменен.";
        return RedirectToPage("/Member/Index");
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
        [Required(ErrorMessage = "Введите текущий пароль.")]
        [DataType(DataType.Password)]
        [Display(Name = "Текущий пароль")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введите новый пароль.")]
        [DataType(DataType.Password)]
        [Display(Name = "Новый пароль")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Подтвердите новый пароль.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "Пароли не совпадают.")]
        [Display(Name = "Подтверждение нового пароля")]
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }
}
