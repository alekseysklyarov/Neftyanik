using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

[Authorize(Roles = RoleNames.Administrator)]
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public CreateModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public MemberInputModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var member = new Neftyanik.Portal.Domain.Entities.Member
        {
            FullName = Input.FullName.Trim(),
            PhoneNumber = Normalize(Input.PhoneNumber),
            Email = Normalize(Input.Email),
            JoinedAt = Input.JoinedAt,
            Notes = Normalize(Input.Notes),
            IsActive = Input.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Members.Add(member);
        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = "Карточка члена товарищества создана.";
        return RedirectToPage("/Administration/Members/Index");
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
