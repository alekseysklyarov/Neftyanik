using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

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
            TempData["ErrorMessage"] = "Связанная учетная запись не найдена.";
            return RedirectToPage("/Administration/Members/Details", new { id = memberId });
        }

        var isAccountant = CurrentRoles.Any(role => string.Equals(role, RoleNames.Accountant, StringComparison.OrdinalIgnoreCase));
        IdentityResult roleUpdateResult;

        if (Input.IsAccountant)
        {
            if (isAccountant)
            {
                TempData["SuccessMessage"] = "Изменения сохранены.";
                return RedirectToPage("/Administration/Members/Details", new { id = memberId });
            }

            if (!await EnsureRoleExistsAsync(RoleNames.Accountant))
            {
                return Page();
            }

            roleUpdateResult = await UserManager.AddToRoleAsync(user, RoleNames.Accountant);
        }
        else
        {
            if (!isAccountant)
            {
                TempData["SuccessMessage"] = "Изменения сохранены.";
                return RedirectToPage("/Administration/Members/Details", new { id = memberId });
            }

            roleUpdateResult = await UserManager.RemoveFromRoleAsync(user, RoleNames.Accountant);
        }

        if (!roleUpdateResult.Succeeded)
        {
            AddIdentityErrors(roleUpdateResult, string.Empty, string.Empty);
            CurrentRoles = (await UserManager.GetRolesAsync(user))
                .OrderBy(role => role)
                .ToArray();
            return Page();
        }

        TempData["SuccessMessage"] = Input.IsAccountant
            ? "Роль бухгалтера назначена."
            : "Роль бухгалтера снята.";

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
            TempData["ErrorMessage"] = "Для этого члена товарищества учетная запись еще не создана.";
            return RedirectToPage("/Administration/Members/Details", new { id = memberId });
        }

        var user = await UserManager.FindByIdAsync(member.ApplicationUserId);
        if (user is null)
        {
            TempData["ErrorMessage"] = "Связанная учетная запись не найдена.";
            return RedirectToPage("/Administration/Members/Details", new { id = memberId });
        }

        var roles = (await UserManager.GetRolesAsync(user))
            .OrderBy(role => role)
            .ToArray();

        Member = member;
        LoginEmail = user.Email ?? user.UserName ?? string.Empty;
        CurrentRoles = roles;

        if (setInput)
        {
            Input.IsAccountant = roles.Any(role => string.Equals(role, RoleNames.Accountant, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private async Task<bool> EnsureRoleExistsAsync(string roleName)
    {
        if (await _roleManager.RoleExistsAsync(roleName))
        {
            return true;
        }

        var createRoleResult = await _roleManager.CreateAsync(new IdentityRole(roleName));
        if (createRoleResult.Succeeded || await _roleManager.RoleExistsAsync(roleName))
        {
            return true;
        }

        foreach (var error in createRoleResult.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return false;
    }

    public sealed class InputModel
    {
        [Display(Name = "Назначить роль бухгалтера")]
        public bool IsAccountant { get; set; }
    }
}
