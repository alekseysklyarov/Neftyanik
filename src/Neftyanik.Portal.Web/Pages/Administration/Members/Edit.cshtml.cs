using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

[Authorize(Roles = RoleNames.Administrator)]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public EditModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public MemberInputModel Input { get; set; } = new();

    public int MemberId { get; private set; }

    public string? LinkedAccount { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var member = await _dbContext.Members
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.FullName,
                item.PhoneNumber,
                item.Email,
                item.JoinedAt,
                item.Notes,
                item.IsActive,
                LinkedAccount = item.ApplicationUserId == null
                    ? null
                    : item.ApplicationUser != null
                        ? item.ApplicationUser.DisplayName ?? item.ApplicationUser.Email ?? item.ApplicationUser.UserName
                        : item.ApplicationUserId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            return NotFound();
        }

        MemberId = member.Id;
        LinkedAccount = member.LinkedAccount;
        Input = new MemberInputModel
        {
            FullName = member.FullName,
            PhoneNumber = member.PhoneNumber,
            Email = member.Email,
            JoinedAt = member.JoinedAt,
            Notes = member.Notes,
            IsActive = member.IsActive
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        MemberId = id;
        LinkedAccount = await GetLinkedAccountAsync(id, cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var member = await _dbContext.Members.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        member.FullName = Input.FullName.Trim();
        member.PhoneNumber = Normalize(Input.PhoneNumber);
        member.Email = Normalize(Input.Email);
        member.JoinedAt = Input.JoinedAt;
        member.Notes = Normalize(Input.Notes);
        member.IsActive = Input.IsActive;
        member.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = "Изменения по члену товарищества сохранены.";
        return RedirectToPage("/Administration/Members/Details", new { id = member.Id });
    }

    private async Task<string?> GetLinkedAccountAsync(int id, CancellationToken cancellationToken)
    {
        return await _dbContext.Members
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => item.ApplicationUserId == null
                ? null
                : item.ApplicationUser != null
                    ? item.ApplicationUser.DisplayName ?? item.ApplicationUser.Email ?? item.ApplicationUser.UserName
                    : item.ApplicationUserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
