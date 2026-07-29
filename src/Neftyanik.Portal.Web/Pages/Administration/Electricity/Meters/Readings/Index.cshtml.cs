using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters.Readings;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class IndexModel : PageModel
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public MeterContextViewModel Meter { get; private set; } = new();

    public IReadOnlyList<ReadingItemViewModel> Readings { get; private set; } = [];

    public int TotalPages { get; private set; } = 1;

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var meter = await _dbContext.MemberElectricityMeters
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new MeterContextViewModel
            {
                Id = item.Id,
                DisplayName = !string.IsNullOrWhiteSpace(item.Name) ? item.Name : !string.IsNullOrWhiteSpace(item.MeterNumber) ? item.MeterNumber : $"Счетчик #{item.Id}",
                BillingPlotNumber = item.BillingPlot != null ? item.BillingPlot.Number : "—",
                HasInitialReading = item.Readings.Any(reading => reading.IsInitialReading),
                IsActive = item.IsActive
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (meter is null)
        {
            return NotFound();
        }

        Meter = meter;
        PageNumber = PageNumber < 1 ? 1 : PageNumber;

        var query = _dbContext.MemberElectricityReadings
            .AsNoTracking()
            .Where(reading => reading.MemberElectricityMeterId == id)
            .OrderByDescending(reading => reading.ReadingDate)
            .ThenByDescending(reading => reading.Id)
            .Select(reading => new ReadingItemViewModel
            {
                ReadingDate = reading.ReadingDate,
                IsInitialReading = reading.IsInitialReading,
                PreviousReading = reading.PreviousReading,
                CurrentReading = reading.CurrentReading,
                Consumption = reading.Consumption,
                AppliedMemberRate = reading.AppliedMemberRate,
                Amount = reading.Amount,
                BillingPlotNumber = reading.Charge != null && reading.Charge.Plot != null
                    ? reading.Charge.Plot.Number
                    : reading.MemberElectricityMeter != null && reading.MemberElectricityMeter.BillingPlot != null
                        ? reading.MemberElectricityMeter.BillingPlot.Number
                        : "—",
                ChargeId = reading.ChargeId,
                IsChargeCancelled = reading.Charge != null && reading.Charge.CancelledAtUtc != null,
                SubmittedByText = reading.SubmittedByMember ? "Участник" : "Администратор"
            });

        var totalCount = await query.CountAsync(cancellationToken);
        TotalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)PageSize);
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        Readings = await query
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        return Page();
    }

    public sealed class MeterContextViewModel
    {
        public int Id { get; init; }
        public string DisplayName { get; init; } = "—";
        public string BillingPlotNumber { get; init; } = "—";
        public bool HasInitialReading { get; init; }
        public bool IsActive { get; init; }
    }

    public sealed class ReadingItemViewModel
    {
        public DateOnly ReadingDate { get; init; }
        public bool IsInitialReading { get; init; }
        public decimal? PreviousReading { get; init; }
        public decimal CurrentReading { get; init; }
        public decimal? Consumption { get; init; }
        public decimal? AppliedMemberRate { get; init; }
        public decimal? Amount { get; init; }
        public string BillingPlotNumber { get; init; } = "—";
        public long? ChargeId { get; init; }
        public bool IsChargeCancelled { get; init; }
        public string SubmittedByText { get; init; } = "—";
        public string EntryTypeText => IsInitialReading ? "Начальные показания" : "Начисление";
        public string ChargeStatusText => IsInitialReading ? "Без начисления" : IsChargeCancelled ? "Начисление отменено" : "Начисление создано";
    }
}
