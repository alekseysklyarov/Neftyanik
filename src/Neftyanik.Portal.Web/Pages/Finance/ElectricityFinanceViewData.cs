using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Finance;

public sealed class ElectricityFinanceSummary
{
    public DateOnly Month { get; init; }
    public IReadOnlyList<ElectricityMeterMonth> Meters { get; init; } = [];
    public int ChargeCount { get; init; }
    public decimal Charged { get; init; }
    public decimal Paid { get; init; }
    public decimal Outstanding { get; init; }
    public decimal TotalOutstanding { get; init; }
    public string StatusClass => ChargeCount == 0 ? "text-muted" : Outstanding > 0 ? "text-danger" : "text-success";
    public string StatusText => ChargeCount == 0
        ? AppLocalizer.Get("Начислений нет", "Нарахувань немає", "No charges")
        : Charged == 0
            ? AppLocalizer.Get("К оплате 0", "До сплати 0", "Nothing to pay")
            : Outstanding == 0
                ? AppLocalizer.Get("Оплачено", "Сплачено", "Paid")
                : Paid > 0
                    ? AppLocalizer.Get("Оплачено частично", "Сплачено частково", "Partially paid")
                    : AppLocalizer.Get("Не оплачено", "Не сплачено", "Unpaid");
}

public sealed record ElectricityMeterMonth(string Name, string PlotNumber, ElectricityMeterReading? Reading,
    ElectricityMeterReading? LatestReading, bool HasCurrentMonthReading);
public sealed record ElectricityMeterReading(DateOnly Date, decimal Day, decimal? Night);
public sealed record ElectricityChargeDetail(DateOnly ChargeDate, string? MeterName,
    ElectricityMeterReading? Previous, ElectricityMeterReading? Current);
public sealed record ElectricityPaymentDetail(
    long ChargeId, DateOnly ChargeDate, string PlotNumber, string? MeterName,
    decimal Allocated, bool IsChargeCancelled,
    ElectricityMeterReading? Previous, ElectricityMeterReading? Current);

// Shared by the administrator and member views. Call only after resolving the authorized member/plots.
public static class ElectricityFinanceViewData
{
    public static async Task<ElectricityFinanceSummary> LoadSummaryAsync(
        ApplicationDbContext db, int memberId, int[] plotIds, DateOnly today, CancellationToken cancellationToken)
    {
        var end = new DateOnly(today.Year, today.Month, 1);
        var start = end.AddMonths(-1);
        var meters = await db.MemberElectricityMeters.AsNoTracking()
            .Where(m => m.MemberId == memberId)
            .Select(m => new { m.Id, m.Name, m.MeterNumber, m.IsActive, PlotNumber = m.BillingPlot != null ? m.BillingPlot.Number : "—" })
            .ToListAsync(cancellationToken);
        var meterIds = meters.Select(m => m.Id).ToArray();
        var readings = await db.MemberElectricityReadings.AsNoTracking()
            .Where(r => meterIds.Contains(r.MemberElectricityMeterId) && r.ReadingDate <= today)
            .OrderByDescending(r => r.ReadingDate).ThenByDescending(r => r.Id)
            .Select(r => new { r.MemberElectricityMeterId, r.ReadingDate, r.CurrentReading, r.CurrentNightReading })
            .ToListAsync(cancellationToken);
        var charges = await db.Charges.AsNoTracking()
            .Where(c => c.CancelledAtUtc == null && c.PlotId.HasValue && plotIds.Contains(c.PlotId.Value)
                && ((c.ChargeType != null && c.ChargeType.Code == ChargeTypeCodes.Electricity) || c.MemberElectricityReading != null))
            .Select(c => new { c.Id, c.ChargeDate, c.PeriodYear, c.PeriodMonth, c.Amount })
            .ToListAsync(cancellationToken);
        var chargeIds = charges.Select(c => c.Id).ToArray();
        var allocations = await db.PaymentAllocations.AsNoTracking()
            .Where(a => chargeIds.Contains(a.ChargeId) && a.Payment != null && a.Payment.CancelledAtUtc == null)
            .Select(a => new { a.ChargeId, a.Amount }).ToListAsync(cancellationToken);
        var paid = allocations.GroupBy(a => a.ChargeId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        // Explicit billing periods take precedence; older electricity charges only have a charge date.
        var monthCharges = charges.Where(c => c.PeriodYear.HasValue && c.PeriodMonth.HasValue
            ? c.PeriodYear == start.Year && c.PeriodMonth == start.Month
            : c.ChargeDate >= start && c.ChargeDate < end).ToList();
        return new ElectricityFinanceSummary
        {
            Month = start,
            Meters = meters.Where(m => m.IsActive || readings.Any(r => r.MemberElectricityMeterId == m.Id && r.ReadingDate >= start))
                .OrderBy(m => m.PlotNumber).ThenBy(m => m.Id).Select(m =>
                {
                    var reading = readings.FirstOrDefault(r => r.MemberElectricityMeterId == m.Id && r.ReadingDate >= start && r.ReadingDate < end);
                    var latest = readings.FirstOrDefault(r => r.MemberElectricityMeterId == m.Id);
                    return new ElectricityMeterMonth(MeterName(m.Name, m.MeterNumber, m.Id), m.PlotNumber,
                        reading == null ? null : new ElectricityMeterReading(reading.ReadingDate, reading.CurrentReading, reading.CurrentNightReading),
                        latest == null ? null : new ElectricityMeterReading(latest.ReadingDate, latest.CurrentReading, latest.CurrentNightReading),
                        latest != null && latest.ReadingDate >= end);
                }).ToList(),
            ChargeCount = monthCharges.Count,
            Charged = monthCharges.Sum(c => c.Amount),
            Paid = monthCharges.Sum(c => Math.Min(c.Amount, paid.GetValueOrDefault(c.Id))),
            Outstanding = monthCharges.Sum(c => Math.Max(0, c.Amount - paid.GetValueOrDefault(c.Id))),
            TotalOutstanding = charges.Sum(c => Math.Max(0, c.Amount - paid.GetValueOrDefault(c.Id)))
        };
    }

    public static async Task<IReadOnlyDictionary<long, IReadOnlyList<ElectricityPaymentDetail>>> LoadPaymentsAsync(
        ApplicationDbContext db, int memberId, long[] paymentIds, CancellationToken cancellationToken)
    {
        if (paymentIds.Length == 0) return new Dictionary<long, IReadOnlyList<ElectricityPaymentDetail>>();
        var allocations = await db.PaymentAllocations.AsNoTracking()
            .Where(a => paymentIds.Contains(a.PaymentId) && a.Payment != null && a.Payment.MemberId == memberId
                && a.Charge != null && ((a.Charge.ChargeType != null && a.Charge.ChargeType.Code == ChargeTypeCodes.Electricity)
                    || a.Charge.MemberElectricityReading != null))
            .Select(a => new
            {
                a.PaymentId, a.ChargeId, a.Amount, a.Charge!.ChargeDate,
                IsCancelled = a.Charge.CancelledAtUtc != null,
                PlotNumber = a.Charge.Plot != null ? a.Charge.Plot.Number : "—"
            }).ToListAsync(cancellationToken);
        var readings = await LoadChargeReadingsAsync(db, allocations.Select(a => a.ChargeId).Distinct().ToArray(), cancellationToken);
        return allocations.GroupBy(a => a.PaymentId).ToDictionary(g => g.Key,
            g => (IReadOnlyList<ElectricityPaymentDetail>)g.OrderBy(a => a.ChargeDate).ThenBy(a => a.ChargeId).Select(a =>
            {
                readings.TryGetValue(a.ChargeId, out var reading);
                return new ElectricityPaymentDetail(a.ChargeId, a.ChargeDate, a.PlotNumber, reading?.MeterName,
                    a.Amount, a.IsCancelled, reading?.Previous, reading?.Current);
            }).ToList());
    }

    public static async Task<IReadOnlyDictionary<long, ElectricityChargeDetail>> LoadChargesAsync(
        ApplicationDbContext db, int[] plotIds, long[] chargeIds, CancellationToken cancellationToken)
    {
        if (chargeIds.Length == 0) return new Dictionary<long, ElectricityChargeDetail>();
        var charges = await db.Charges.AsNoTracking()
            .Where(c => chargeIds.Contains(c.Id) && c.PlotId.HasValue && plotIds.Contains(c.PlotId.Value)
                && ((c.ChargeType != null && c.ChargeType.Code == ChargeTypeCodes.Electricity) || c.MemberElectricityReading != null))
            .Select(c => new { c.Id, c.ChargeDate }).ToListAsync(cancellationToken);
        var readings = await LoadChargeReadingsAsync(db, charges.Select(c => c.Id).ToArray(), cancellationToken);
        return charges.ToDictionary(c => c.Id, c => readings.GetValueOrDefault(c.Id)
            ?? new ElectricityChargeDetail(c.ChargeDate, null, null, null));
    }

    private static async Task<Dictionary<long, ElectricityChargeDetail>> LoadChargeReadingsAsync(
        ApplicationDbContext db, long[] chargeIds, CancellationToken cancellationToken)
    {
        if (chargeIds.Length == 0) return [];
        var meterIds = await db.MemberElectricityReadings.AsNoTracking()
            .Where(r => r.ChargeId.HasValue && chargeIds.Contains(r.ChargeId.Value))
            .Select(r => r.MemberElectricityMeterId).Distinct().ToArrayAsync(cancellationToken);
        var history = await db.MemberElectricityReadings.AsNoTracking()
            .Where(r => meterIds.Contains(r.MemberElectricityMeterId))
            .OrderBy(r => r.ReadingDate).ThenBy(r => r.Id)
            .Select(r => new
            {
                r.Id, r.ChargeId, ChargeDate = r.Charge != null ? r.Charge.ChargeDate : r.ReadingDate,
                r.MemberElectricityMeterId, r.ReadingDate, r.CurrentReading, r.CurrentNightReading, r.IsInitialReading,
                Name = r.MemberElectricityMeter != null ? r.MemberElectricityMeter.Name : null,
                Number = r.MemberElectricityMeter != null ? r.MemberElectricityMeter.MeterNumber : null
            }).ToListAsync(cancellationToken);
        var byId = new Dictionary<long, ElectricityChargeDetail>();
        var requestedChargeIds = chargeIds.ToHashSet();
        foreach (var group in history.GroupBy(r => r.MemberElectricityMeterId))
        {
            ElectricityMeterReading? previous = null;
            foreach (var r in group)
            {
                var current = new ElectricityMeterReading(r.ReadingDate, r.CurrentReading, r.CurrentNightReading);
                if (r.ChargeId.HasValue && requestedChargeIds.Contains(r.ChargeId.Value))
                {
                    byId[r.ChargeId.Value] = new ElectricityChargeDetail(r.ChargeDate,
                        MeterName(r.Name, r.Number, r.MemberElectricityMeterId), r.IsInitialReading ? null : previous, current);
                }
                previous = current;
            }
        }
        return byId;
    }

    private static string MeterName(string? name, string? number, int id) => !string.IsNullOrWhiteSpace(name) ? name
        : !string.IsNullOrWhiteSpace(number) ? number
        : AppLocalizer.Get($"Счётчик #{id}", $"Лічильник #{id}", $"Meter #{id}");
}
