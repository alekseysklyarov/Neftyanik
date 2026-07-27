using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;

public class CancelChargeModel : PlotFinancePageModelBase
{
    public CancelChargeModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
        : base(dbContext, userManager)
    {
    }

    [BindProperty]
    public CancellationInputModel Input { get; set; } = new();

    public PlotFinanceContextViewModel Plot { get; private set; } = new();

    public ChargeContextViewModel Charge { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int plotId, long id, CancellationToken cancellationToken)
    {
        return await LoadPageAsync(plotId, id, cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(int plotId, long id, CancellationToken cancellationToken)
    {
        var charge = await DbContext.Charges
            .Include(item => item.ChargeType)
            .FirstOrDefaultAsync(item => item.Id == id && item.PlotId == plotId, cancellationToken);

        if (charge is null)
        {
            return NotFound();
        }

        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        Plot = plot;
        Charge = new ChargeContextViewModel
        {
            Id = charge.Id,
            ChargeTypeName = charge.ChargeType?.Name ?? "—",
            Amount = charge.Amount,
            ChargeDate = charge.ChargeDate,
            IsCancelled = charge.CancelledAtUtc != null
        };

        if (charge.CancelledAtUtc != null)
        {
            ModelState.AddModelError(string.Empty, "Это начисление уже отменено.");
            return Page();
        }

        charge.CancelledAtUtc = DateTime.UtcNow;
        charge.CancellationReason = Normalize(Input.CancellationReason);
        await DbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = "Начисление отменено.";
        return RedirectToPage("/Administration/Plots/Finance/Index", new { plotId });
    }

    private async Task<IActionResult> LoadPageAsync(int plotId, long id, CancellationToken cancellationToken)
    {
        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        var charge = await DbContext.Charges
            .AsNoTracking()
            .Where(item => item.Id == id && item.PlotId == plotId)
            .Select(item => new ChargeContextViewModel
            {
                Id = item.Id,
                ChargeTypeName = item.ChargeType != null ? item.ChargeType.Name : "—",
                Amount = item.Amount,
                ChargeDate = item.ChargeDate,
                IsCancelled = item.CancelledAtUtc != null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (charge is null)
        {
            return NotFound();
        }

        Plot = plot;
        Charge = charge;
        return Page();
    }

    public sealed class ChargeContextViewModel
    {
        public long Id { get; init; }

        public string ChargeTypeName { get; init; } = string.Empty;

        public decimal Amount { get; init; }

        public DateOnly ChargeDate { get; init; }

        public bool IsCancelled { get; init; }
    }
}
