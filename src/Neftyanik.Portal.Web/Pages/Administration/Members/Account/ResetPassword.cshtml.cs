using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;

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
            TempData["ErrorMessage"] = AppLocalizer.Get("Для этого члена товарищества учетная запись еще не создана.", "Для цього члена товариства обліковий запис ще не створено.", "An account has not been created for this member yet.");
            return RedirectToPage("/Administration/Members/Details", new { id = memberId });
        }

        var user = await UserManager.FindByIdAsync(member.ApplicationUserId);
        if (user is null)
        {
            TempData["ErrorMessage"] = AppLocalizer.Get("Связанная учетная запись не найдена.", "Пов'язаний обліковий запис не знайдено.", "The linked account was not found.");
            return RedirectToPage("/Administration/Members/Details", new { id = memberId });
        }

        Member = member;
        LoginEmail = user.Email ?? user.UserName ?? string.Empty;

        ValidateInput();

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
            ? AppLocalizer.Get("Временный пароль установлен. Пользователь должен изменить его при следующем входе.", "Тимчасовий пароль встановлено. Користувач повинен змінити його під час наступного входу.", "The temporary password has been set. The user must change it on the next sign-in.")
            : AppLocalizer.Get("Временный пароль установлен.", "Тимчасовий пароль встановлено.", "The temporary password has been set.");
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
            TempData["ErrorMessage"] = AppLocalizer.Get("Для этого члена товарищества учетная запись еще не создана.", "Для цього члена товариства обліковий запис ще не створено.", "An account has not been created for this member yet.");
            return RedirectToPage("/Administration/Members/Details", new { id = memberId });
        }

        var user = await UserManager.FindByIdAsync(member.ApplicationUserId);
        if (user is null)
        {
            TempData["ErrorMessage"] = AppLocalizer.Get("Связанная учетная запись не найдена.", "Пов'язаний обліковий запис не знайдено.", "The linked account was not found.");
            return RedirectToPage("/Administration/Members/Details", new { id = memberId });
        }

        Member = member;
        LoginEmail = user.Email ?? user.UserName ?? string.Empty;
        return Page();
    }

    private void ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(Input.NewTemporaryPassword))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.NewTemporaryPassword)}", AppLocalizer.Get("Введите новый временный пароль.", "Введіть новий тимчасовий пароль.", "Enter a new temporary password."));
        }

        if (string.IsNullOrWhiteSpace(Input.ConfirmPassword))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.ConfirmPassword)}", AppLocalizer.Get("Подтвердите новый временный пароль.", "Підтвердьте новий тимчасовий пароль.", "Confirm the new temporary password."));
        }

        if (!string.IsNullOrWhiteSpace(Input.NewTemporaryPassword)
            && !string.IsNullOrWhiteSpace(Input.ConfirmPassword)
            && !string.Equals(Input.NewTemporaryPassword, Input.ConfirmPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.ConfirmPassword)}", AppLocalizer.Get("Пароли не совпадают.", "Паролі не збігаються.", "Passwords do not match."));
        }
    }

    public class InputModel
    {
        [DataType(DataType.Password)]
        public string NewTemporaryPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;

        public bool MustChangePasswordOnLogin { get; set; }
    }
}
