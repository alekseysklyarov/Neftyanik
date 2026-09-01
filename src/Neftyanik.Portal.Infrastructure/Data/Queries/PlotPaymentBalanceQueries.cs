using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Neftyanik.Portal.Infrastructure.Data.Queries;

public static class PlotPaymentBalanceQueries
{
    public static async Task<Dictionary<int, decimal>> LoadActivePaymentTotalsByPlotAsync(
        this ApplicationDbContext dbContext,
        IEnumerable<int> plotIds,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.LoadActivePaymentTotalsByPlotAsync(plotIds, memberId: null, cancellationToken);
    }

    public static async Task<Dictionary<int, decimal>> LoadActivePaymentTotalsByPlotAsync(
        this ApplicationDbContext dbContext,
        IEnumerable<int> plotIds,
        int? memberId,
        CancellationToken cancellationToken = default)
    {
        var plotIdArray = plotIds.Distinct().ToArray();
        if (plotIdArray.Length == 0)
        {
            return [];
        }

        var activePayments = await dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.CancelledAtUtc == null
                && payment.PlotId.HasValue
                && (!memberId.HasValue || payment.MemberId == memberId.Value)
                && plotIdArray.Contains(payment.PlotId.Value))
            .Select(payment => new ActivePaymentItem(
                payment.Id,
                payment.PlotId!.Value,
                payment.Amount))
            .ToListAsync(cancellationToken);

        var activeAllocations = await dbContext.PaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.Payment != null
                && allocation.Payment.CancelledAtUtc == null
                && (!memberId.HasValue || allocation.Payment.MemberId == memberId.Value)
                && allocation.Charge != null
                && allocation.Charge.CancelledAtUtc == null
                && ((allocation.Payment.PlotId.HasValue && plotIdArray.Contains(allocation.Payment.PlotId.Value))
                    || (allocation.Charge.PlotId.HasValue && plotIdArray.Contains(allocation.Charge.PlotId.Value))))
            .Select(allocation => new ActiveAllocationItem(
                allocation.PaymentId,
                allocation.Payment!.PlotId,
                allocation.Charge!.PlotId,
                allocation.Amount))
            .ToListAsync(cancellationToken);

        var totalsByPlot = activeAllocations
            .Where(allocation => allocation.ChargePlotId.HasValue && plotIdArray.Contains(allocation.ChargePlotId.Value))
            .GroupBy(allocation => allocation.ChargePlotId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(allocation => allocation.Amount));

        var allocatedByPaymentId = activeAllocations
            .Where(allocation => allocation.PaymentPlotId.HasValue && plotIdArray.Contains(allocation.PaymentPlotId.Value))
            .GroupBy(allocation => allocation.PaymentId)
            .ToDictionary(group => group.Key, group => group.Sum(allocation => allocation.Amount));

        foreach (var payment in activePayments)
        {
            var availableAmount = payment.Amount - allocatedByPaymentId.GetValueOrDefault(payment.PaymentId);
            if (availableAmount <= 0m)
            {
                continue;
            }

            totalsByPlot[payment.PlotId] = totalsByPlot.GetValueOrDefault(payment.PlotId) + availableAmount;
        }

        return totalsByPlot;
    }

    private sealed record ActivePaymentItem(long PaymentId, int PlotId, decimal Amount);

    private sealed record ActiveAllocationItem(long PaymentId, int? PaymentPlotId, int? ChargePlotId, decimal Amount);
}
