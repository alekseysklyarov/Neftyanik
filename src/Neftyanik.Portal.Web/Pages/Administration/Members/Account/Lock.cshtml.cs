using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

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
        Account = BuildAccountContext(user);

        var currentUser = await UserManager.GetUserAsync(User);
        if (!Account.IsLockedOut && currentUser?.Id == user.Id)
        {
            ModelState.AddModelError(string.Empty, "Нельзя заблокировать текущую учетную запись администратора через эту операцию.");
            return Page();
        }

        if (Account.IsLockedOut)
        {
            var unlockResult = await UserManager.SetLockoutEndDateAsync(user, null);
            if (!unlockResult.Succeeded)
            {
                AddIdentityErrors(unlockResult, string.Empty, string.Empty);
                return Page();
            }

            TempData["SuccessMessage"] = "Учетная запись пользователя разблокирована.";
        }
        else
        {
            if (!user.LockoutEnabled)
            {
                var enableLockoutResult = await UserManager.SetLockoutEnabledAsync(user, true);
                if (!enableLockoutResult.Succeeded)
                {
                    AddIdentityErrors(enableLockoutResult, string.Empty, string.Empty);
                    return Page();
                }
            }

            var lockResult = await UserManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
            if (!lockResult.Succeeded)
            {
                AddIdentityErrors(lockResult, string.Empty, string.Empty);
                return Page();
            }

            TempData["SuccessMessage"] = "Учетная запись пользователя заблокирована.";
        }

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
        Account = BuildAccountContext(user);
        return Page();
    }

    private static AccountContextViewModel BuildAccountContext(ApplicationUser user)
    {
        var isLockedOut = user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;

        return new AccountContextViewModel
        {
            LoginEmail = user.Email ?? user.UserName ?? string.Empty,
            IsLockedOut = isLockedOut,
            LockoutEnd = user.LockoutEnd
        };
    }

    public sealed class AccountContextViewModel
    {
        public string LoginEmail { get; init; } = string.Empty;

        public bool IsLockedOut { get; init; }

        public DateTimeOffset? LockoutEnd { get; init; }
    }
}
