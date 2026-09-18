using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Identity;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Members.Account;

public class LockModel : MemberAccountPageModelBase
{
    public LockModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
        : base(dbContext, userManager)
    {
    }

    public MemberContextViewModel Member { get; private set; } = new();

    public AccountContextViewModel Account { get; private set; } = new();

    public bool IsUnlockOperation => Account.IsLockedOut;

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
        Account = await BuildAccountContextAsync(user, cancellationToken);

        var currentUser = await UserManager.GetUserAsync(User);
        if (!Account.IsLockedOut && currentUser?.Id == user.Id)
        {
            ModelState.AddModelError(string.Empty, AppLocalizer.Get("Нельзя заблокировать текущую учетную запись администратора через эту операцию.", "Не можна заблокувати поточний обліковий запис адміністратора через цю операцію.", "The current administrator account cannot be locked using this operation."));
            return Page();
        }

        if (Account.IsGloballyLockedOut)
        {
            if (!await AssociationAccountAccess.CanManageGlobalAccountAsync(DbContext, user.Id, cancellationToken))
            {
                return Forbid();
            }

            var result = await UserManager.SetLockoutEndDateAsync(user, null);
            if (!result.Succeeded)
            {
                AddIdentityErrors(result, string.Empty, string.Empty);
                return Page();
            }
        }

        var memberships = await DbContext.AssociationUserMemberships
            .Where(x => x.ApplicationUserId == user.Id).ToListAsync(cancellationToken);
        foreach (var membership in memberships)
        {
            membership.IsActive = Account.IsLockedOut && member.IsActive;
        }
        await DbContext.SaveChangesAsync(cancellationToken);
        TempData["SuccessMessage"] = AppLocalizer.Get("Изменения сохранены.", "Зміни збережено.", "Changes have been saved.");

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
        Account = await BuildAccountContextAsync(user, cancellationToken);
        return Page();
    }

    private async Task<AccountContextViewModel> BuildAccountContextAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var isLockedOut = !await DbContext.AssociationUserMemberships.AsNoTracking()
            .AnyAsync(x => x.ApplicationUserId == user.Id && x.IsActive, cancellationToken);
        var isGloballyLockedOut = user.LockoutEnabled && user.LockoutEnd > DateTimeOffset.UtcNow;

        return new AccountContextViewModel
        {
            LoginEmail = user.Email ?? user.UserName ?? string.Empty,
            IsLockedOut = isLockedOut || isGloballyLockedOut,
            IsGloballyLockedOut = isGloballyLockedOut,
            LockoutEnd = isGloballyLockedOut ? user.LockoutEnd : null
        };
    }

    public sealed class AccountContextViewModel
    {
        public string LoginEmail { get; init; } = string.Empty;

        public bool IsLockedOut { get; init; }

        public bool IsGloballyLockedOut { get; init; }

        public DateTimeOffset? LockoutEnd { get; init; }
    }
}
