using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Ownerships;

public class EndModel : OwnershipPageModelBase
{
    public EndModel(ApplicationDbContext dbContext)
        : base(dbContext)
    {
    }

    [BindProperty]
    public OwnershipEndInputModel Input { get; set; } = new();

    public PlotContextViewModel Plot { get; private set; } = new();

    public OwnershipContextViewModel Ownership { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int plotId, int id, CancellationToken cancellationToken)
    {
        var result = await LoadPageAsync(plotId, id, cancellationToken);
        if (result is PageResult && !Input.ValidTo.HasValue)
        {
            Input.ValidTo = DateOnly.FromDateTime(DateTime.Now);
        }

        return result;
    }

    public async Task<IActionResult> OnPostAsync(int plotId, int id, CancellationToken cancellationToken)
    {
        var ownership = await DbContext.PlotOwnerships
            .Include(item => item.Member)
            .FirstOrDefaultAsync(item => item.Id == id && item.PlotId == plotId, cancellationToken);

        if (ownership is null)
        {
            return NotFound();
        }

        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        Plot = plot;
        Ownership = new OwnershipContextViewModel
        {
            Id = ownership.Id,
            PlotId = ownership.PlotId,
            MemberId = ownership.MemberId,
            MemberFullName = ownership.Member?.FullName ?? "—",
            OwnershipShare = ownership.OwnershipShare,
            IsPrimaryContact = ownership.IsPrimaryContact,
            ValidFrom = ownership.ValidFrom,
            ValidTo = ownership.ValidTo
        };

        if (ownership.ValidTo.HasValue)
        {
            ModelState.AddModelError(string.Empty, "Эта запись владения уже завершена.");
            return Page();
        }

        ValidateDateRange(ownership.ValidFrom, Input.ValidTo);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        ownership.ValidTo = Input.ValidTo;
        await DbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = "Владение участком успешно завершено.";
        return RedirectToPage("/Administration/Plots/Ownerships/Index", new { plotId });
    }

    private async Task<IActionResult> LoadPageAsync(int plotId, int id, CancellationToken cancellationToken)
    {
        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        var ownership = await DbContext.PlotOwnerships
            .AsNoTracking()
            .Where(item => item.Id == id && item.PlotId == plotId)
            .Select(item => new OwnershipContextViewModel
            {
                Id = item.Id,
                PlotId = item.PlotId,
                MemberId = item.MemberId,
                MemberFullName = item.Member != null ? item.Member.FullName : "—",
                OwnershipShare = item.OwnershipShare,
                IsPrimaryContact = item.IsPrimaryContact,
                ValidFrom = item.ValidFrom,
                ValidTo = item.ValidTo
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (ownership is null)
        {
            return NotFound();
        }

        Plot = plot;
        Ownership = ownership;
        return Page();
    }

    public sealed class OwnershipContextViewModel
    {
        public int Id { get; init; }

        public int PlotId { get; init; }

        public int MemberId { get; init; }

        public string MemberFullName { get; init; } = string.Empty;

        public decimal? OwnershipShare { get; init; }

        public bool IsPrimaryContact { get; init; }

        public DateOnly? ValidFrom { get; init; }

        public DateOnly? ValidTo { get; init; }
    }
}
