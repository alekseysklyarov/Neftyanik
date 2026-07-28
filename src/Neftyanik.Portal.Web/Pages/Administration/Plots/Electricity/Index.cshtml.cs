using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Electricity;

public class IndexModel : PlotFinancePageModelBase
{
    private const int PageSize = 20;

    public IndexModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
        : base(dbContext, userManager)
    {
    }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public PlotFinanceContextViewModel Plot { get; private set; } = new();

    public IReadOnlyList<ElectricityReadingItemViewModel> Readings { get; private set; } = [];

    public int TotalPages { get; private set; } = 1;

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public bool HasHistory => Readings.Count > 0;

    public async Task<IActionResult> OnGetAsync(int plotId, CancellationToken cancellationToken)
    {
        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        Plot = plot;
        PageNumber = PageNumber < 1 ? 1 : PageNumber;

        var query = DbContext.ElectricityReadings
            .AsNoTracking()
            .Where(reading => reading.PlotId == plotId)
            .OrderByDescending(reading => reading.ReadingDate)
            .ThenByDescending(reading => reading.Id);

        var totalCount = await query.CountAsync(cancellationToken);
        TotalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)PageSize);
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        var readings = await query
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(reading => new ElectricityReadingItemViewModel
            {
                Id = reading.Id,
                ReadingDate = reading.ReadingDate,
                IsInitialReading = reading.IsInitialReading,
                CurrentDayReading = reading.CurrentDayReading,
                DayConsumption = reading.DayConsumption,
                CurrentNightReading = reading.CurrentNightReading,
                NightConsumption = reading.NightConsumption,
                DayRate = reading.DayRate,
                NightRate = reading.NightRate,
                TotalAmount = reading.TotalAmount,
                ChargeId = reading.ChargeId
            })
            .ToListAsync(cancellationToken);

        var chargeIds = readings
            .Where(reading => reading.ChargeId.HasValue)
            .Select(reading => reading.ChargeId!.Value)
            .ToArray();

        HashSet<long> cancelledChargeIdSet = [];
        if (chargeIds.Length > 0)
        {
            cancelledChargeIdSet = (await DbContext.Charges
                .AsNoTracking()
                .Where(charge => chargeIds.Contains(charge.Id) && charge.CancelledAtUtc != null)
                .Select(charge => charge.Id)
                .ToListAsync(cancellationToken))
                .ToHashSet();
        }

        foreach (var reading in readings)
        {
            reading.IsChargeCancelled = reading.ChargeId.HasValue && cancelledChargeIdSet.Contains(reading.ChargeId.Value);
        }

        Readings = readings;

        return Page();
    }

    public sealed class ElectricityReadingItemViewModel
    {
        public long Id { get; init; }

        public DateOnly ReadingDate { get; init; }

        public bool IsInitialReading { get; init; }

        public decimal CurrentDayReading { get; init; }

        public decimal? DayConsumption { get; init; }

        public decimal CurrentNightReading { get; init; }

        public decimal? NightConsumption { get; init; }

        public decimal? DayRate { get; init; }

        public decimal? NightRate { get; init; }

        public decimal? TotalAmount { get; init; }

        public long? ChargeId { get; init; }

        public bool IsChargeCancelled { get; set; }

        public string EntryTypeText => IsInitialReading ? "Начальные показания" : "Начисление";

        public string ChargeStatusText => ChargeId switch
        {
            null => "Без начисления",
            _ when IsChargeCancelled => "Начисление отменено",
            _ => "Начисление создано"
        };
    }
}
