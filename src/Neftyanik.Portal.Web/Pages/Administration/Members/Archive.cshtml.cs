using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

[Authorize(Roles = RoleNames.Administrator)]
public class ArchiveModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public ArchiveModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public MemberArchiveViewModel Member { get; private set; } = new();

    public bool IsArchiveOperation => Member.IsActive;

    public bool CanChangeStatus => !IsArchiveOperation || Member.ActiveOwnershipsCount == 0;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var member = await GetViewModelAsync(id, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        Member = member;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        var member = await _dbContext.Members.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        if (member.IsActive)
        {
            var hasActiveOwnerships = await _dbContext.PlotOwnerships
                .AsNoTracking()
                .AnyAsync(ownership => ownership.MemberId == id && ownership.ValidTo == null, cancellationToken);

            if (hasActiveOwnerships)
            {
                Member = (await GetViewModelAsync(id, cancellationToken))!;
                ModelState.AddModelError(string.Empty, "Нельзя архивировать члена товарищества, пока у него есть активные владения участками.");
                return Page();
            }
        }

        var willArchive = member.IsActive;
        member.IsActive = !member.IsActive;
        member.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = willArchive
            ? "Член товарищества переведён в архив. Запись не была удалена."
            : "Член товарищества восстановлен из архива.";

        return RedirectToPage("/Administration/Members/Details", new { id = member.Id });
    }

    private async Task<MemberArchiveViewModel?> GetViewModelAsync(int id, CancellationToken cancellationToken)
    {
        return await _dbContext.Members
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new MemberArchiveViewModel
            {
                Id = item.Id,
                FullName = item.FullName,
                Email = item.Email,
                IsActive = item.IsActive,
                ActiveOwnershipsCount = item.PlotOwnerships.Count(ownership => ownership.ValidTo == null)
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public sealed class MemberArchiveViewModel
    {
        public int Id { get; init; }

        public string FullName { get; init; } = string.Empty;

        public string? Email { get; init; }

        public bool IsActive { get; init; }

        public int ActiveOwnershipsCount { get; init; }
    }
}
