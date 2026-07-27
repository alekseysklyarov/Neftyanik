using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots;

[Authorize(Roles = RoleNames.Administrator)]
public class ArchiveModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public ArchiveModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public PlotArchiveViewModel Plot { get; private set; } = new();

    public bool IsArchiveOperation => Plot.IsActive;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var plot = await GetViewModelAsync(id, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        Plot = plot;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        var plot = await _dbContext.Plots.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        var willArchive = plot.IsActive;
        plot.IsActive = !plot.IsActive;
        plot.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = willArchive
            ? "Участок переведён в архив. Запись не была удалена."
            : "Участок восстановлен из архива.";

        return RedirectToPage("/Administration/Plots/Details", new { id = plot.Id });
    }

    private async Task<PlotArchiveViewModel?> GetViewModelAsync(int id, CancellationToken cancellationToken)
    {
        return await _dbContext.Plots
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new PlotArchiveViewModel
            {
                Id = item.Id,
                Number = item.Number,
                Address = item.Address,
                IsActive = item.IsActive
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public sealed class PlotArchiveViewModel
    {
        public int Id { get; init; }

        public string Number { get; init; } = string.Empty;

        public string? Address { get; init; }

        public bool IsActive { get; init; }
    }
}
