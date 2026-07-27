using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ChargeTypes;

[Authorize(Roles = RoleNames.Administrator)]
public class ArchiveModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public ArchiveModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public ChargeTypeArchiveViewModel ChargeType { get; private set; } = new();

    public bool IsArchiveOperation => ChargeType.IsActive;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var chargeType = await LoadViewModelAsync(id, cancellationToken);
        if (chargeType is null)
        {
            return NotFound();
        }

        ChargeType = chargeType;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        var chargeType = await _dbContext.ChargeTypes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (chargeType is null)
        {
            return NotFound();
        }

        var willArchive = chargeType.IsActive;
        chargeType.IsActive = !chargeType.IsActive;
        chargeType.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = willArchive
            ? "Тип начисления переведен в архив."
            : "Тип начисления восстановлен из архива.";

        return RedirectToPage("/Administration/Finance/ChargeTypes/Details", new { id });
    }

    private async Task<ChargeTypeArchiveViewModel?> LoadViewModelAsync(int id, CancellationToken cancellationToken)
    {
        return await _dbContext.ChargeTypes
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new ChargeTypeArchiveViewModel
            {
                Id = item.Id,
                Name = item.Name,
                IsActive = item.IsActive,
                ChargesCount = item.Charges.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public sealed class ChargeTypeArchiveViewModel
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public bool IsActive { get; init; }

        public int ChargesCount { get; init; }
    }
}
