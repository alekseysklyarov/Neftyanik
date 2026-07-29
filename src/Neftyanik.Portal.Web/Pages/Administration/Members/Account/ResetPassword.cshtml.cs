using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Members.Account;

public class ResetPasswordModel : MemberAccountPageModelBase
{
    public ResetPasswordModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
        : base(dbContext, userManager)
    {
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public MemberContextViewModel Member { get; private set; } = new();

    public string LoginEmail { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int memberId, CancellationToken cancellationToken)
    {
        return await LoadPageAsync(memberId, cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(int memberId, CancellationToken cancellationToken)
    {
        var member = await GetMemberContextAsync(memberId, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(member.ApplicationUserId))
        {
            TempData["ErrorMessage"] = "Для этого члена товарищества учетная запись еще не создана.";
            return RedirectToPage("/Administration/Members/Details", new { id = memberId });
        }

        var user = await UserManager.FindByIdAsync(member.ApplicationUserId);
        if (user is null)
        {
            TempData["ErrorMessage"] = "Связанная учетная запись не найдена.";
            return RedirectToPage("/Administration/Members/Details", new { id = memberId });
        }

        Member = member;
        LoginEmail = user.Email ?? user.UserName ?? string.Empty;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var resetToken = await UserManager.GeneratePasswordResetTokenAsync(user);
        user.MustChangePassword = Input.MustChangePasswordOnLogin;
        var resetResult = await UserManager.ResetPasswordAsync(user, resetToken, Input.NewTemporaryPassword);
        if (!resetResult.Succeeded)
        {
            AddIdentityErrors(resetResult, string.Empty, "Input.NewTemporaryPassword");
            return Page();
        }

        TempData["SuccessMessage"] = Input.MustChangePasswordOnLogin
            ? "Временный пароль установлен. Пользователь должен изменить его при следующем входе."
            : "Временный пароль установлен.";
        return RedirectToPage("/Administration/Members/Details", new { id = memberId });
    }

    private async Task<IActionResult> LoadPageAsync(int memberId, CancellationToken cancellationToken)
    {
        var member = await GetMemberContextAsync(memberId, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(member.ApplicationUserId))
        {
            TempData["ErrorMessage"] = "Для этого члена товарищества учетная запись еще не создана.";
            return RedirectToPage("/Administration/Members/Details", new { id = memberId });
        }

        var user = await UserManager.FindByIdAsync(member.ApplicationUserId);
        if (user is null)
        {
            TempData["ErrorMessage"] = "Связанная учетная запись не найдена.";
            return RedirectToPage("/Administration/Members/Details", new { id = memberId });
        }

        Member = member;
        LoginEmail = user.Email ?? user.UserName ?? string.Empty;
        return Page();
    }

    public class InputModel
    {
        [Required(ErrorMessage = "Введите новый временный пароль.")]
        [DataType(DataType.Password)]
        [Display(Name = "Новый временный пароль")]
        public string NewTemporaryPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Подтвердите новый временный пароль.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewTemporaryPassword), ErrorMessage = "Пароли не совпадают.")]
        [Display(Name = "Подтверждение пароля")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Display(Name = "Сменить при входе")]
        public bool MustChangePasswordOnLogin { get; set; }
    }
}
