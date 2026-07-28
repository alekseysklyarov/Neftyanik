using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters;

[Authorize(Roles = RoleNames.Administrator)]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<MeterItemViewModel> Meters { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Today);
        var meters = await _dbContext.MemberElectricityMeters
            .AsNoTracking()
            .OrderBy(meter => meter.Member != null ? meter.Member.FullName : string.Empty)
            .ThenBy(meter => meter.Name)
            .ThenBy(meter => meter.MeterNumber)
            .Select(meter => new MeterItemViewModel
            {
                Id = meter.Id,
                MemberName = meter.Member != null ? meter.Member.FullName : "—",
                MeterNumber = meter.MeterNumber,
                Name = meter.Name,
                IsActive = meter.IsActive,
                BillingPlotId = meter.BillingPlotId,
                BillingPlotNumber = meter.BillingPlot != null ? meter.BillingPlot.Number : "—",
                LinkedPlotNumbers = meter.MeterPlots.OrderBy(link => link.Plot != null ? link.Plot.Number : string.Empty)
                    .Select(link => link.Plot != null ? link.Plot.Number : "—")
                    .ToList(),
                LatestReadingDate = meter.Readings.OrderByDescending(reading => reading.ReadingDate).Select(reading => (DateOnly?)reading.ReadingDate).FirstOrDefault(),
                LatestAmount = meter.Readings.OrderByDescending(reading => reading.ReadingDate).Select(reading => reading.Amount).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var activeOwnerships = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentOn(currentDate)
            .Select(ownership => new { ownership.MemberId, ownership.PlotId })
            .ToListAsync(cancellationToken);

        foreach (var meter in meters)
        {
            var ownedPlotIds = activeOwnerships.Where(item => item.MemberId == meter.MemberId).Select(item => item.PlotId).ToHashSet();
            meter.HasOwnershipWarning = meter.LinkedPlotIds.Any(plotId => !ownedPlotIds.Contains(plotId));
        }

        Meters = meters;
    }

    public sealed class MeterItemViewModel
    {
        public int Id { get; init; }
        public int MemberId { get; init; }
        public string MemberName { get; init; } = "—";
        public string? MeterNumber { get; init; }
        public string? Name { get; init; }
        public bool IsActive { get; init; }
        public int BillingPlotId { get; init; }
        public string BillingPlotNumber { get; init; } = "—";
        public List<string> LinkedPlotNumbers { get; init; } = [];
        public List<int> LinkedPlotIds { get; init; } = [];
        public DateOnly? LatestReadingDate { get; init; }
        public decimal? LatestAmount { get; init; }
        public bool HasOwnershipWarning { get; set; }
        public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : !string.IsNullOrWhiteSpace(MeterNumber) ? MeterNumber : $"Счетчик #{Id}";
    }
}
