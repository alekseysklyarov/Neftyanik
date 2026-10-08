using Microsoft.EntityFrameworkCore;

namespace Neftyanik.Portal.Infrastructure.Data.Queries;

public sealed record OutstandingPaymentCharge(
    long Id, int ChargeTypeId, string ChargeTypeName, DateOnly ChargeDate, decimal Amount, decimal AllocatedAmount)
{
    public decimal OutstandingAmount => Amount - AllocatedAmount;
}

public static class OutstandingPaymentChargeQueries
{
    public static async Task<IReadOnlyList<OutstandingPaymentCharge>> LoadOutstandingPaymentChargesAsync(
        this ApplicationDbContext db, int[] plotIds, CancellationToken cancellationToken = default, int? memberId = null)
    {
        if (plotIds.Length == 0) return [];

        var charges = await db.Charges.AsNoTracking()
            .Where(c => (!memberId.HasValue || c.MemberId == memberId) && c.CancelledAtUtc == null && c.PlotId.HasValue && plotIds.Contains(c.PlotId.Value))
            .OrderBy(c => c.ChargeDate).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.ChargeTypeId, ChargeTypeName = c.ChargeType != null ? c.ChargeType.Name : "—", c.ChargeDate, c.Amount })
            .ToListAsync(cancellationToken);
        var chargeIds = charges.Select(c => c.Id).ToArray();
        var allocations = await db.PaymentAllocations.AsNoTracking()
            .Where(a => chargeIds.Contains(a.ChargeId) && a.Payment != null && a.Payment.CancelledAtUtc == null)
            .Select(a => new { a.ChargeId, a.Amount }).ToListAsync(cancellationToken);
        var paid = allocations.GroupBy(a => a.ChargeId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));

        return charges.Select(c => new OutstandingPaymentCharge(c.Id, c.ChargeTypeId, c.ChargeTypeName,
                c.ChargeDate, c.Amount, paid.GetValueOrDefault(c.Id)))
            .Where(c => c.OutstandingAmount > 0m).ToList();
    }
}
