using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Ownerships;

public class IndexModel : OwnershipPageModelBase
{
    public IndexModel(ApplicationDbContext dbContext)
        : base(dbContext)
    {
    }

    public PlotContextViewModel Plot { get; private set; } = new();

    public IReadOnlyList<OwnershipListItemViewModel> CurrentOwnerships { get; private set; } = [];

    public IReadOnlyList<OwnershipListItemViewModel> HistoricalOwnerships { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int plotId, CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);

        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        var ownerships = await DbContext.PlotOwnerships
            .AsNoTracking()
            .Where(ownership => ownership.PlotId == plotId)
            .OrderByDescending(ownership => ownership.ValidTo == null)
            .ThenBy(ownership => ownership.ValidTo)
            .ThenBy(ownership => ownership.Member != null ? ownership.Member.FullName : string.Empty)
            .Select(ownership => new OwnershipListItemViewModel
            {
                Id = ownership.Id,
                PlotId = ownership.PlotId,
                MemberId = ownership.MemberId,
                MemberFullName = ownership.Member != null ? ownership.Member.FullName : "—",
                MemberPhoneNumber = ownership.Member != null ? ownership.Member.PhoneNumber : null,
                MemberEmail = ownership.Member != null ? ownership.Member.Email : null,
                OwnershipShare = ownership.OwnershipShare,
                ValidFrom = ownership.ValidFrom,
                ValidTo = ownership.ValidTo,
                IsActive = (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                    && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate)
            })
            .ToListAsync(cancellationToken);

        Plot = plot;
        CurrentOwnerships = ownerships.Where(ownership => ownership.IsActive).ToList();
        HistoricalOwnerships = ownerships.Where(ownership => !ownership.IsActive).ToList();
        return Page();
    }

    public sealed class OwnershipListItemViewModel
    {
        public int Id { get; init; }

        public int PlotId { get; init; }

        public int MemberId { get; init; }

        public string MemberFullName { get; init; } = string.Empty;

        public string? MemberPhoneNumber { get; init; }

        public string? MemberEmail { get; init; }

        public decimal? OwnershipShare { get; init; }

        public DateOnly? ValidFrom { get; init; }

        public DateOnly? ValidTo { get; init; }

        public bool IsActive { get; init; }
    }
}
