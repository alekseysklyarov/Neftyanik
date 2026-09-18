using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Members.Account;

public class RolesModel : MemberAccountPageModelBase
{
    private readonly RoleManager<IdentityRole> _roleManager;

    public RolesModel(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
        : base(dbContext, userManager)
    {
        _roleManager = roleManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public MemberContextViewModel Member { get; private set; } = new();

    public string LoginEmail { get; private set; } = string.Empty;

    public IReadOnlyList<string> CurrentRoles { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int memberId, CancellationToken cancellationToken)
    {
        var loadResult = await LoadPageStateAsync(memberId, cancellationToken, setInput: true);
        return loadResult ?? Page();
    }

    public async Task<IActionResult> OnPostAsync(int memberId, CancellationToken cancellationToken)
    {
        var loadResult = await LoadPageStateAsync(memberId, cancellationToken, setInput: false);
        if (loadResult is not null)
        {
            return loadResult;
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await UserManager.FindByIdAsync(Member.ApplicationUserId!);
        if (user is null)
        {
            TempData["ErrorMessage"] = AppLocalizer.Get("Связанная учетная запись не найдена.", "Пов'язаний обліковий запис не знайдено.", "The linked account was not found.");
            return RedirectToPage("/Administration/Members/Details", new { id = memberId });
        }

        var membership = await DbContext.AssociationUserMemberships
            .SingleOrDefaultAsync(x => x.ApplicationUserId == user.Id && x.Role == RoleNames.Accountant, cancellationToken);
        if (Input.IsAccountant && membership is null)
        {
            DbContext.AssociationUserMemberships.Add(new AssociationUserMembership
            {
                ApplicationUserId = user.Id, Role = RoleNames.Accountant
            });
        }
        else if (membership is not null)
        {
            if (Input.IsAccountant)
            {
                membership.IsActive = true;
            }
            else
            {
                DbContext.AssociationUserMemberships.Remove(membership);
            }
        }
        await DbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = Input.IsAccountant
            ? AppLocalizer.Get("Роль бухгалтера назначена.", "Роль бухгалтера призначено.", "The accountant role has been assigned.")
            : AppLocalizer.Get("Роль бухгалтера снята.", "Роль бухгалтера знято.", "The accountant role has been removed.");

        return RedirectToPage("/Administration/Members/Details", new { id = memberId });
    }

    private async Task<IActionResult?> LoadPageStateAsync(int memberId, CancellationToken cancellationToken, bool setInput)
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

        var roles = await DbContext.AssociationUserMemberships.AsNoTracking()
            .Where(x => x.ApplicationUserId == user.Id && x.IsActive)
            .Select(x => x.Role).OrderBy(role => role).ToArrayAsync(cancellationToken);

        Member = member;
        LoginEmail = user.Email ?? user.UserName ?? string.Empty;
        CurrentRoles = roles;

        if (setInput)
        {
            Input.IsAccountant = roles.Any(role => string.Equals(role, RoleNames.Accountant, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    public sealed class InputModel
    {
        public bool IsAccountant { get; set; }
    }
}
