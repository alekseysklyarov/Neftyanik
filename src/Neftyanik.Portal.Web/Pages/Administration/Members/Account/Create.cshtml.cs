using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Members.Account;

public class CreateModel : MemberAccountPageModelBase
{
    public CreateModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
        : base(dbContext, userManager)
    {
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public MemberContextViewModel Member { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int memberId, CancellationToken cancellationToken)
    {
        var member = await GetMemberContextAsync(memberId, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(member.ApplicationUserId))
        {
            TempData["ErrorMessage"] = AppLocalizer.Get(
                "Для этого члена товарищества учетная запись уже создана.",
                "Для цього члена товариства обліковий запис уже створено.",
                "An account has already been created for this member.");
            return RedirectToPage("/Administration/Members/Details", new { id = memberId });
        }

        Member = member;
        Input.Login = member.Email ?? string.Empty;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int memberId, CancellationToken cancellationToken)
    {
        var member = await DbContext.Members.FirstOrDefaultAsync(item => item.Id == memberId, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        Member = new MemberContextViewModel
        {
            Id = member.Id,
            FullName = member.FullName,
            Email = member.Email,
            PhoneNumber = member.PhoneNumber,
            IsActive = member.IsActive,
            ApplicationUserId = member.ApplicationUserId
        };

        if (!member.IsActive)
        {
            ModelState.AddModelError(string.Empty, AppLocalizer.Get(
                "Нельзя создать учетную запись для архивного члена товарищества.",
                "Не можна створити обліковий запис для архівного члена товариства.",
                "An account cannot be created for an archived member."));
        }

        if (!string.IsNullOrWhiteSpace(member.ApplicationUserId))
        {
            ModelState.AddModelError(string.Empty, AppLocalizer.Get(
                "Для этого члена товарищества учетная запись уже создана.",
                "Для цього члена товариства обліковий запис уже створено.",
                "An account has already been created for this member."));
        }

        ValidateInput();

        var login = Input.Login.Trim();
        var existingLoginUser = await UserManager.FindByNameAsync(login);
        if (existingLoginUser is not null)
        {
            ModelState.AddModelError("Input.Login", AppLocalizer.Get(
                "Пользователь с таким логином уже существует.",
                "Користувач із таким логіном уже існує.",
                "A user with this login already exists."));
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var name = ParseFullName(member.FullName);

        var user = new ApplicationUser
        {
            UserName = login,
            Email = null,
            EmailConfirmed = false,
            FirstName = name.FirstName,
            LastName = name.LastName,
            DisplayName = name.DisplayName,
            PhoneNumber = member.PhoneNumber,
            IsActive = member.IsActive,
            CreatedAt = DateTimeOffset.UtcNow,
            LockoutEnabled = true,
            MustChangePassword = Input.MustChangePasswordOnLogin
        };

        await using var transaction = await DbContext.Database.BeginTransactionAsync(cancellationToken);

        var createResult = await UserManager.CreateAsync(user, Input.TemporaryPassword);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            AddIdentityErrors(createResult, "Input.Login", "Input.TemporaryPassword");
            return Page();
        }

        var addToRoleResult = await UserManager.AddToRoleAsync(user, RoleNames.Member);
        if (!addToRoleResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            AddIdentityErrors(addToRoleResult, "Input.Login", "Input.TemporaryPassword");
            return Page();
        }

        member.ApplicationUserId = user.Id;
        member.UpdatedAtUtc = DateTime.UtcNow;
        await DbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (TempData is not null)
        {
            TempData["SuccessMessage"] = AppLocalizer.Get(
                "Учетная запись члена товарищества успешно создана.",
                "Обліковий запис члена товариства успішно створено.",
                "The member account has been created successfully.");
        }

        return RedirectToPage("/Administration/Members/Details", new { id = memberId });
    }

    private void ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(Input.Login))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.Login)}", AppLocalizer.Get(
                "Укажите логин для входа.",
                "Вкажіть логін для входу.",
                "Enter a login."));
        }

        if (string.IsNullOrWhiteSpace(Input.TemporaryPassword))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.TemporaryPassword)}", AppLocalizer.Get(
                "Введите временный пароль.",
                "Введіть тимчасовий пароль.",
                "Enter a temporary password."));
        }

        if (string.IsNullOrWhiteSpace(Input.ConfirmPassword))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.ConfirmPassword)}", AppLocalizer.Get(
                "Подтвердите временный пароль.",
                "Підтвердіть тимчасовий пароль.",
                "Confirm the temporary password."));
        }

        if (!string.IsNullOrWhiteSpace(Input.TemporaryPassword)
            && !string.IsNullOrWhiteSpace(Input.ConfirmPassword)
            && !string.Equals(Input.TemporaryPassword, Input.ConfirmPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.ConfirmPassword)}", AppLocalizer.Get(
                "Пароли не совпадают.",
                "Паролі не збігаються.",
                "The passwords do not match."));
        }
    }

    public class InputModel
    {
        public string Login { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        public string TemporaryPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;

        public bool MustChangePasswordOnLogin { get; set; }
    }
}
