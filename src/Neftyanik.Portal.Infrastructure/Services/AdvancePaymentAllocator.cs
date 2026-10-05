using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public static class AdvancePaymentAllocator
{
    // Use the same transaction lock for payment registration and charge creation.
    // Otherwise two simultaneous charges can consume the same advance.
    public static async Task LockAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsSqlServer()) return;
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Payment allocation requires a transaction.");

        var resource = $"DachaHub.PaymentAllocation:{db.CurrentAssociationId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51023, 'Payment allocation lock could not be acquired.', 1;
            """, cancellationToken);
    }

    public static async Task<decimal> ApplyAsync(ApplicationDbContext db, IFinancialAuditService audit,
        IEnumerable<long> chargeIds, CancellationToken cancellationToken = default)
    {
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        await LockAsync(db, cancellationToken);

        var ids = chargeIds.Distinct().ToArray();
        var charges = await db.Charges.AsNoTracking()
            .Where(c => ids.Contains(c.Id) && c.CancelledAtUtc == null && c.PlotId.HasValue)
            .OrderBy(c => c.ChargeDate).ThenBy(c => c.Id).ToListAsync(cancellationToken);
        var plotIds = charges.Select(c => c.PlotId!.Value).Distinct().ToArray();
        var ownerships = await db.PlotOwnerships.AsNoTracking()
            .Where(o => plotIds.Contains(o.PlotId)).ToListAsync(cancellationToken);
        var memberIds = ownerships.Select(o => o.MemberId).Distinct().ToArray();
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.MemberId.HasValue && memberIds.Contains(p.MemberId.Value) && p.CancelledAtUtc == null)
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.Id).ToListAsync(cancellationToken);
        var paymentIds = payments.Select(p => p.Id).ToArray();
        var allocations = await db.PaymentAllocations.AsNoTracking()
            .Where(a => (paymentIds.Contains(a.PaymentId) || ids.Contains(a.ChargeId))
                && a.Payment != null && a.Payment.CancelledAtUtc == null
                && a.Charge != null && a.Charge.CancelledAtUtc == null)
            .Select(a => new { a.PaymentId, a.ChargeId, a.Amount }).ToListAsync(cancellationToken);
        var used = allocations.GroupBy(a => a.PaymentId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        var paid = allocations.GroupBy(a => a.ChargeId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        decimal total = 0;

        foreach (var charge in charges)
        {
            var remaining = charge.Amount - paid.GetValueOrDefault(charge.Id);
            var owners = ownerships.Where(o => o.PlotId == charge.PlotId
                && (!o.ValidFrom.HasValue || o.ValidFrom <= charge.ChargeDate)
                && (!o.ValidTo.HasValue || o.ValidTo >= charge.ChargeDate))
                .Select(o => o.MemberId).ToHashSet();
            foreach (var payment in payments.Where(p => owners.Contains(p.MemberId!.Value)))
            {
                if (remaining <= 0) break;
                var available = payment.Amount - used.GetValueOrDefault(payment.Id);
                if (available <= 0) continue;
                var amount = Math.Min(remaining, available);
                db.PaymentAllocations.Add(new PaymentAllocation { PaymentId = payment.Id, ChargeId = charge.Id, Amount = amount });
                used[payment.Id] = used.GetValueOrDefault(payment.Id) + amount;
                remaining -= amount;
                total += amount;
                audit.Add(FinancialAuditLogActions.Updated, nameof(Charge), charge.Id.ToString(),
                    $"Зачтён аванс из платежа #{payment.Id} в начисление #{charge.Id}.",
                    newValues: new { PaymentId = payment.Id, ChargeId = charge.Id, Amount = amount, Source = "Advance" });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return total;
    }
}
