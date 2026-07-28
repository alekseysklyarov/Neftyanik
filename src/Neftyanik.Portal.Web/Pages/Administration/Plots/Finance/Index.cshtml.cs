using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;

public class IndexModel : PlotFinancePageModelBase
{
    public IndexModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
        : base(dbContext, userManager)
    {
    }

    public PlotFinanceContextViewModel Plot { get; private set; } = new();

    public IReadOnlyList<ChargeItemViewModel> Charges { get; private set; } = [];

    public IReadOnlyList<PaymentItemViewModel> Payments { get; private set; } = [];

    public ElectricitySummaryViewModel Electricity { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int plotId, CancellationToken cancellationToken)
    {
        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        Plot = plot;

        Charges = await DbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.PlotId == plotId)
            .OrderByDescending(charge => charge.ChargeDate)
            .ThenByDescending(charge => charge.Id)
            .Select(charge => new ChargeItemViewModel
            {
                Id = charge.Id,
                ChargeTypeName = charge.ChargeType != null ? charge.ChargeType.Name : "—",
                Amount = charge.Amount,
                ChargeDate = charge.ChargeDate,
                DueDate = charge.DueDate,
                PeriodYear = charge.PeriodYear,
                PeriodMonth = charge.PeriodMonth,
                Description = charge.Description,
                IsCancelled = charge.CancelledAtUtc != null,
                CancelledAtUtc = charge.CancelledAtUtc,
                CancellationReason = charge.CancellationReason
            })
            .ToListAsync(cancellationToken);

        Payments = await DbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.PlotId == plotId)
            .OrderByDescending(payment => payment.PaymentDate)
            .ThenByDescending(payment => payment.Id)
            .Select(payment => new PaymentItemViewModel
            {
                Id = payment.Id,
                Amount = payment.Amount,
                PaymentDate = payment.PaymentDate,
                PaymentMethodText = GetPaymentMethodText(payment.PaymentMethod),
                ReferenceNumber = payment.ReferenceNumber,
                Description = payment.Description,
                IsCancelled = payment.CancelledAtUtc != null,
                CancelledAtUtc = payment.CancelledAtUtc,
                CancellationReason = payment.CancellationReason
            })
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var latestReading = await DbContext.ElectricityReadings
            .AsNoTracking()
            .Where(reading => reading.PlotId == plotId)
            .OrderByDescending(reading => reading.ReadingDate)
            .ThenByDescending(reading => reading.Id)
            .Select(reading => new ElectricitySummaryViewModel
            {
                HasHistory = true,
                LatestReadingDate = reading.ReadingDate,
                LatestDayReading = reading.CurrentDayReading,
                LatestNightReading = reading.CurrentNightReading,
                IsLatestInitialReading = reading.IsInitialReading,
                LatestChargeAmount = reading.TotalAmount
            })
            .FirstOrDefaultAsync(cancellationToken);

        var currentTariff = await DbContext.ElectricityTariffs
            .AsNoTracking()
            .Where(tariff => tariff.EffectiveFrom <= today)
            .OrderByDescending(tariff => tariff.EffectiveFrom)
            .ThenByDescending(tariff => tariff.Id)
            .Select(tariff => new { tariff.DayRate, tariff.NightRate, tariff.EffectiveFrom })
            .FirstOrDefaultAsync(cancellationToken);

        Electricity = latestReading ?? new ElectricitySummaryViewModel();
        Electricity.CurrentDayRate = currentTariff?.DayRate;
        Electricity.CurrentNightRate = currentTariff?.NightRate;
        Electricity.CurrentTariffEffectiveFrom = currentTariff?.EffectiveFrom;

        return Page();
    }

    public sealed class ElectricitySummaryViewModel
    {
        public bool HasHistory { get; init; }

        public DateOnly? LatestReadingDate { get; init; }

        public decimal? LatestDayReading { get; init; }

        public decimal? LatestNightReading { get; init; }

        public bool IsLatestInitialReading { get; init; }

        public decimal? LatestChargeAmount { get; init; }

        public decimal? CurrentDayRate { get; set; }

        public decimal? CurrentNightRate { get; set; }

        public DateOnly? CurrentTariffEffectiveFrom { get; set; }
    }

    public sealed class ChargeItemViewModel
    {
        public long Id { get; init; }

        public string ChargeTypeName { get; init; } = string.Empty;

        public decimal Amount { get; init; }

        public DateOnly ChargeDate { get; init; }

        public DateOnly? DueDate { get; init; }

        public int? PeriodYear { get; init; }

        public int? PeriodMonth { get; init; }

        public string? Description { get; init; }

        public bool IsCancelled { get; init; }

        public DateTime? CancelledAtUtc { get; init; }

        public string? CancellationReason { get; init; }
    }

    public sealed class PaymentItemViewModel
    {
        public long Id { get; init; }

        public decimal Amount { get; init; }

        public DateOnly PaymentDate { get; init; }

        public string PaymentMethodText { get; init; } = string.Empty;

        public string? ReferenceNumber { get; init; }

        public string? Description { get; init; }

        public bool IsCancelled { get; init; }

        public DateTime? CancelledAtUtc { get; init; }

        public string? CancellationReason { get; init; }
    }
}
