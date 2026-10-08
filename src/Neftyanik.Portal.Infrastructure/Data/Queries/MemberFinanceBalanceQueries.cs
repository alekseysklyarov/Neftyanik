using Microsoft.EntityFrameworkCore;

namespace Neftyanik.Portal.Infrastructure.Data.Queries;

public static class MemberFinanceBalanceQueries
{
    public static Task<int[]> LoadMemberFinancePlotIdsAsync(this ApplicationDbContext dbContext,
        int memberId, CancellationToken cancellationToken = default) =>
        dbContext.PlotOwnerships.Where(o => o.MemberId == memberId).Select(o => o.PlotId)
            .Union(dbContext.Charges.Where(c => c.MemberId == memberId && c.PlotId.HasValue).Select(c => c.PlotId!.Value))
            .Union(dbContext.Payments.Where(p => p.MemberId == memberId && p.PlotId.HasValue).Select(p => p.PlotId!.Value))
            .ToArrayAsync(cancellationToken);

    public static async Task<decimal> CalculateActiveBalanceAsync(
        this ApplicationDbContext dbContext,
        int memberId,
        IEnumerable<int> plotIds,
        CancellationToken cancellationToken = default)
    {
        var chargeAmounts = await dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.CancelledAtUtc == null
                && charge.MemberId == memberId)
            .Select(charge => charge.Amount)
            .ToListAsync(cancellationToken);

        var paymentAmounts = await dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.MemberId == memberId
                && payment.CancelledAtUtc == null)
            .Select(payment => payment.Amount)
            .ToListAsync(cancellationToken);

        var totalCharges = chargeAmounts.Sum();
        var totalPayments = paymentAmounts.Sum();

        return totalCharges - totalPayments;
    }
}
