using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

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
            TempData["ErrorMessage"] = "Для этого члена товарищества учетная запись уже создана.";
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
            ModelState.AddModelError(string.Empty, "Нельзя создать учетную запись для архивного члена товарищества.");
        }

        if (!string.IsNullOrWhiteSpace(member.ApplicationUserId))
        {
            ModelState.AddModelError(string.Empty, "Для этого члена товарищества учетная запись уже создана.");
        }

        var login = Input.Login.Trim();
        var existingLoginUser = await UserManager.FindByNameAsync(login);
        if (existingLoginUser is not null)
        {
            ModelState.AddModelError("Input.Login", "Пользователь с таким логином уже существует.");
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
            TempData["SuccessMessage"] = "Учетная запись члена товарищества успешно создана.";
        }

        return RedirectToPage("/Administration/Members/Details", new { id = memberId });
    }

    public class InputModel
    {
        [Required(ErrorMessage = "Укажите логин для входа.")]
        [Display(Name = "Логин")]
        public string Login { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введите временный пароль.")]
        [DataType(DataType.Password)]
        [Display(Name = "Временный пароль")]
        public string TemporaryPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Подтвердите временный пароль.")]
        [DataType(DataType.Password)]
        [Compare(nameof(TemporaryPassword), ErrorMessage = "Пароли не совпадают.")]
        [Display(Name = "Подтверждение пароля")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Display(Name = "Сменить пароль при следующем входе")]
        public bool MustChangePasswordOnLogin { get; set; }
    }
}
