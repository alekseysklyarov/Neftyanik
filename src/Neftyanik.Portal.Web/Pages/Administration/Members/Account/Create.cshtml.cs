using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
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
        Input.LoginEmail = member.Email ?? string.Empty;
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

        var loginEmail = Input.LoginEmail.Trim();
        var existingUser = await UserManager.FindByEmailAsync(loginEmail) ?? await UserManager.FindByNameAsync(loginEmail);
        if (existingUser is not null)
        {
            ModelState.AddModelError("Input.LoginEmail", "Пользователь с таким адресом электронной почты уже существует.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var name = ParseFullName(member.FullName);

        var user = new ApplicationUser
        {
            UserName = loginEmail,
            Email = loginEmail,
            EmailConfirmed = false,
            FirstName = name.FirstName,
            LastName = name.LastName,
            DisplayName = name.DisplayName,
            PhoneNumber = member.PhoneNumber,
            IsActive = member.IsActive,
            CreatedAt = DateTimeOffset.UtcNow,
            LockoutEnabled = true,
            MustChangePassword = true
        };

        await using var transaction = await DbContext.Database.BeginTransactionAsync(cancellationToken);

        var createResult = await UserManager.CreateAsync(user, Input.TemporaryPassword);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            AddIdentityErrors(createResult, "Input.LoginEmail", "Input.TemporaryPassword");
            return Page();
        }

        var addToRoleResult = await UserManager.AddToRoleAsync(user, RoleNames.Member);
        if (!addToRoleResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            AddIdentityErrors(addToRoleResult, "Input.LoginEmail", "Input.TemporaryPassword");
            return Page();
        }

        member.ApplicationUserId = user.Id;
        member.UpdatedAtUtc = DateTime.UtcNow;
        await DbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        TempData["SuccessMessage"] = "Учетная запись члена товарищества успешно создана.";
        return RedirectToPage("/Administration/Members/Details", new { id = memberId });
    }

    public class InputModel
    {
        [Required(ErrorMessage = "Укажите адрес электронной почты для входа.")]
        [EmailAddress(ErrorMessage = "Введите корректный адрес электронной почты.")]
        [Display(Name = "Логин / email")]
        public string LoginEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введите временный пароль.")]
        [DataType(DataType.Password)]
        [Display(Name = "Временный пароль")]
        public string TemporaryPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Подтвердите временный пароль.")]
        [DataType(DataType.Password)]
        [Compare(nameof(TemporaryPassword), ErrorMessage = "Пароли не совпадают.")]
        [Display(Name = "Подтверждение пароля")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
