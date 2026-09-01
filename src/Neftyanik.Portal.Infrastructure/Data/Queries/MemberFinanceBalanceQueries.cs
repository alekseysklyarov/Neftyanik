using Microsoft.EntityFrameworkCore;

namespace Neftyanik.Portal.Infrastructure.Data.Queries;

public static class MemberFinanceBalanceQueries
{
    public static async Task<decimal> CalculateActiveBalanceAsync(
        this ApplicationDbContext dbContext,
        int memberId,
        IEnumerable<int> plotIds,
        CancellationToken cancellationToken = default)
    {
        var plotIdArray = plotIds.Distinct().ToArray();
        if (plotIdArray.Length == 0)
        {
            return 0m;
        }

        var chargeAmounts = await dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.CancelledAtUtc == null
                && charge.PlotId.HasValue
                && plotIdArray.Contains(charge.PlotId.Value))
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
