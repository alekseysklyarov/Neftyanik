using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Queries;

public static class PlotFinanceQueries
{
    public static IQueryable<PlotFinanceSummaryProjection> SelectFinanceSummary(this IQueryable<Plot> query)
    {
        return query.Select(plot => new PlotFinanceSummaryProjection
        {
            PlotId = plot.Id,
            PlotNumber = plot.Number,
            PlotAddress = plot.Address,
            ActiveChargesTotal = plot.Charges
                .Where(charge => charge.CancelledAtUtc == null)
                .Sum(charge => (decimal?)charge.Amount) ?? 0m,
            ActivePaymentsTotal = plot.Payments
                .Where(payment => payment.CancelledAtUtc == null)
                .Sum(payment => (decimal?)payment.Amount) ?? 0m
        });
    }
}

public sealed class PlotFinanceSummaryProjection
{
    public int PlotId { get; init; }

    public string PlotNumber { get; init; } = string.Empty;

    public string? PlotAddress { get; init; }

    [Precision(18, 2)]
    public decimal ActiveChargesTotal { get; init; }

    [Precision(18, 2)]
    public decimal ActivePaymentsTotal { get; init; }

    public decimal Balance => ActiveChargesTotal - ActivePaymentsTotal;
}
