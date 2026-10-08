using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class MemberReadingCorrectionService(ApplicationDbContext db, IFinancialAuditService audit)
{
    public async Task<ElectricityOperationResult> CorrectAsync(int meterId, long readingId, decimal day, decimal? night,
        string reason, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor) || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return ElectricityOperationResult.Failure("Укажите причину исправления (до 500 символов).");
        if (day < 0 || decimal.Truncate(day) != day || (night.HasValue && (night < 0 || decimal.Truncate(night.Value) != night)))
            return ElectricityOperationResult.Failure("Показания должны быть целыми неотрицательными числами.");
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        await AdvancePaymentAllocator.LockAsync(db, ct);
        var readings = await db.MemberElectricityReadings.Include(r => r.Charge)
            .Where(r => r.MemberElectricityMeterId == meterId).OrderBy(r => r.ReadingDate).ThenBy(r => r.Id).ToListAsync(ct);
        var index = readings.FindIndex(r => r.Id == readingId);
        if (index < 0) return ElectricityOperationResult.Failure("Показания не найдены.");
        if (index != readings.Count - 1)
            return ElectricityOperationResult.Failure("Можно исправить только последнее показание счётчика. Обновите страницу участника.");
        var target = readings[index];
        if (target.CurrentNightReading.HasValue != night.HasValue)
            return ElectricityOperationResult.Failure("Укажите показания всех зон счётчика.");
        if (index > 0 && (day < readings[index - 1].CurrentReading || night < readings[index - 1].CurrentNightReading))
            return ElectricityOperationResult.Failure("Показания не могут быть меньше предыдущих.");
        if (day == target.CurrentReading && night == target.CurrentNightReading)
            return ElectricityOperationResult.Failure("Показания не изменились.");
        var affectedIndexes = new[] { index };
        foreach (var i in affectedIndexes)
            if (!readings[i].IsInitialReading && (i == 0 || readings[i].Charge is null || !readings[i].Charge!.MemberId.HasValue || !readings[i].AppliedMemberRate.HasValue
                || (readings[i].CurrentNightReading.HasValue && !readings[i].AppliedMemberNightRate.HasValue)))
                return ElectricityOperationResult.Failure("Недостаточно данных для пересчёта начисления.");
        var oldReadings = affectedIndexes.ToDictionary(i => readings[i].Id, i => new
            { readings[i].CurrentReading, readings[i].CurrentNightReading, readings[i].PhysicalMeterReadingsJson, readings[i].Amount, readings[i].ChargeId });
        target.CurrentReading = day;
        target.CurrentNightReading = night;
        // A correction of the total cannot retain an obsolete physical breakdown.
        // The original breakdown remains available in the financial audit.
        target.PhysicalMeterReadingsJson = null;
        var newCharges = new List<Charge>();
        // Only the latest reading may be corrected; earlier intervals remain unchanged.
        foreach (var i in affectedIndexes)
        {
            var reading = readings[i];
            if (reading.IsInitialReading) continue;
            var previous = readings[i - 1];
            var amount = Math.Round((reading.CurrentReading - previous.CurrentReading) * reading.AppliedMemberRate!.Value, 2, MidpointRounding.AwayFromZero)
                + Math.Round(((reading.CurrentNightReading ?? 0m) - (previous.CurrentNightReading ?? 0m)) * (reading.AppliedMemberNightRate ?? 0m), 2, MidpointRounding.AwayFromZero);
            var oldCharge = reading.Charge!;
            oldCharge.CancelledAtUtc ??= DateTime.UtcNow;
            var cancellationReason = "Исправление показаний: " + reason.Trim();
            oldCharge.CancellationReason = cancellationReason[..Math.Min(500, cancellationReason.Length)];
            var replacement = new Charge
            {
                MemberId = oldCharge.MemberId, PlotId = oldCharge.PlotId, ChargeTypeId = oldCharge.ChargeTypeId,
                Amount = amount, ChargeDate = oldCharge.ChargeDate, DueDate = oldCharge.DueDate,
                PeriodYear = oldCharge.PeriodYear, PeriodMonth = oldCharge.PeriodMonth,
                Description = $"Электроэнергия за {reading.ReadingDate:dd.MM.yyyy}. Исправление начисления #{oldCharge.Id}.",
                CreatedByUserId = actor
            };
            reading.Charge = replacement;
            reading.Amount = amount;
            db.Charges.Add(replacement);
            newCharges.Add(replacement);
            audit.Add(FinancialAuditLogActions.Cancelled, nameof(Charge), oldCharge.Id.ToString(),
                oldCharge.CancellationReason, oldValues: new { oldCharge.Amount }, newValues: new { ReplacementAmount = amount, ReadingId = reading.Id, Reason = reason.Trim() });
        }
        await db.SaveChangesAsync(ct);
        await AdvancePaymentAllocator.ApplyAsync(db, audit, newCharges.Select(c => c.Id), ct);
        foreach (var i in affectedIndexes)
        {
            var reading = readings[i];
            audit.Add(FinancialAuditLogActions.Updated, nameof(MemberElectricityReading), reading.Id.ToString(),
                "Исправлены показания и расчёт: " + reason.Trim(), oldReadings[reading.Id],
                new { reading.CurrentReading, reading.CurrentNightReading, reading.PhysicalMeterReadingsJson, reading.Amount, reading.ChargeId, CorrectedReadingId = target.Id });
        }
        foreach (var charge in newCharges)
            audit.Add(FinancialAuditLogActions.Created, nameof(Charge), charge.Id.ToString(), charge.Description,
                newValues: new { charge.MemberId, charge.Amount, charge.ChargeDate, CorrectedReadingId = target.Id });
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return ElectricityOperationResult.Success();
    }
}
