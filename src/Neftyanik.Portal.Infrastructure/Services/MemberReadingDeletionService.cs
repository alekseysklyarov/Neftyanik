using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class MemberReadingDeletionService(ApplicationDbContext db, IFinancialAuditService audit)
{
    public async Task<MemberReadingDeletionResult> DeleteLatestAsync(int meterId, long readingId,
        string? reason, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor))
            return new(MemberReadingDeletionResultCode.InvalidActor);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return new(MemberReadingDeletionResultCode.InvalidReason);

        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        // Serialize with reading creation, correction and payment allocation.
        await AdvancePaymentAllocator.LockAsync(db, ct);
        var target = await db.MemberElectricityReadings.Include(r => r.Charge)
            .SingleOrDefaultAsync(r => r.Id == readingId && r.MemberElectricityMeterId == meterId, ct);
        if (target is null) return new(MemberReadingDeletionResultCode.NotFound);
        if (await db.MemberElectricityReadings.AnyAsync(r => r.MemberElectricityMeterId == meterId
            && (r.ReadingDate > target.ReadingDate || (r.ReadingDate == target.ReadingDate && r.Id > target.Id)), ct))
            return new(MemberReadingDeletionResultCode.NotLatest);

        var oldValues = new
        {
            target.Id, target.AssociationId, target.MemberElectricityMeterId, target.ReadingDate,
            target.CurrentReading, target.CurrentNightReading, target.PhysicalMeterReadingsJson,
            target.AppliedMemberRate, target.AppliedMemberNightRate, target.Amount, target.IsInitialReading,
            target.ChargeId, target.CreatedAtUtc, target.CreatedByUserId, target.SubmittedByMember
        };
        if (target.ChargeId.HasValue && target.Charge is null)
            return new(MemberReadingDeletionResultCode.ChargeCancellationFailed);
        if (target.Charge is { CancelledAtUtc: null })
        {
            var cancellationReason = "Удаление последнего показания: " + reason.Trim();
            var cancelled = await new ChargeService(db, audit).CancelChargeAsync(
                new(target.ChargeId!.Value, cancellationReason[..Math.Min(500, cancellationReason.Length)]), ct);
            if (!cancelled.Succeeded)
                return new(MemberReadingDeletionResultCode.ChargeCancellationFailed);
        }

        // Retain cancelled charges and their allocations as financial history.
        // Existing balance/advance calculations exclude allocations to cancelled charges.
        db.MemberElectricityReadings.Remove(target);
        audit.Add(FinancialAuditLogActions.Deleted, nameof(MemberElectricityReading), target.Id.ToString(),
            "Удалено последнее показание счётчика: " + reason.Trim(), oldValues,
            new { Reason = reason.Trim(), DeletedByUserId = actor, CancelledChargeId = target.ChargeId });
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new(MemberReadingDeletionResultCode.Success);
    }
}
