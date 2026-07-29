using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public DetailsModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public MeterDetailsViewModel Meter { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Today);
        var meter = await _dbContext.MemberElectricityMeters
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new MeterDetailsViewModel
            {
                Id = item.Id,
                MemberId = item.MemberId,
                MemberName = item.Member != null ? item.Member.FullName : "—",
                MeterNumber = item.MeterNumber,
                Name = item.Name,
                IsActive = item.IsActive,
                BillingPlotId = item.BillingPlotId,
                BillingPlotNumber = item.BillingPlot != null ? item.BillingPlot.Number : "—",
                LinkedPlots = item.MeterPlots
                    .OrderBy(link => link.Plot != null ? link.Plot.Number : string.Empty)
                    .Select(link => new LinkedPlotViewModel
                    {
                        PlotId = link.PlotId,
                        PlotNumber = link.Plot != null ? link.Plot.Number : "—"
                    })
                    .ToList(),
                LatestReadingDate = item.Readings
                    .OrderByDescending(reading => reading.ReadingDate)
                    .ThenByDescending(reading => reading.Id)
                    .Select(reading => (DateOnly?)reading.ReadingDate)
                    .FirstOrDefault(),
                LatestReading = item.Readings
                    .OrderByDescending(reading => reading.ReadingDate)
                    .ThenByDescending(reading => reading.Id)
                    .Select(reading => (decimal?)reading.CurrentReading)
                    .FirstOrDefault(),
                LatestChargedAmount = item.Readings
                    .OrderByDescending(reading => reading.ReadingDate)
                    .ThenByDescending(reading => reading.Id)
                    .Where(reading => !reading.IsInitialReading)
                    .Select(reading => reading.Amount)
                    .FirstOrDefault(),
                ReadingCount = item.Readings.Count,
                HasInitialReading = item.Readings.Any(reading => reading.IsInitialReading)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (meter is null)
        {
            return NotFound();
        }

        var activeOwnedPlotIds = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(meter.MemberId, currentDate)
            .Select(ownership => ownership.PlotId)
            .Distinct()
            .ToListAsync(cancellationToken);

        meter.HasOwnershipWarning = meter.LinkedPlots.Any(link => !activeOwnedPlotIds.Contains(link.PlotId));
        Meter = meter;
        return Page();
    }

    public sealed class MeterDetailsViewModel
    {
        public int Id { get; init; }
        public int MemberId { get; init; }
        public string MemberName { get; init; } = "—";
        public string? MeterNumber { get; init; }
        public string? Name { get; init; }
        public bool IsActive { get; init; }
        public int BillingPlotId { get; init; }
        public string BillingPlotNumber { get; init; } = "—";
        public IReadOnlyList<LinkedPlotViewModel> LinkedPlots { get; init; } = [];
        public DateOnly? LatestReadingDate { get; init; }
        public decimal? LatestReading { get; init; }
        public int ReadingCount { get; init; }
        public decimal? LatestChargedAmount { get; init; }
        public bool HasInitialReading { get; init; }
        public bool HasOwnershipWarning { get; set; }
        public bool CanCreateReading => HasInitialReading && IsActive;
        public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : !string.IsNullOrWhiteSpace(MeterNumber) ? MeterNumber : $"Счетчик #{Id}";
    }

    public sealed class LinkedPlotViewModel
    {
        public int PlotId { get; init; }
        public string PlotNumber { get; init; } = "—";
    }
}
