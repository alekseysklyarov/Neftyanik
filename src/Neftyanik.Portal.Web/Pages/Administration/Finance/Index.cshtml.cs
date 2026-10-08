using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Web.Pages.Finance;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public FinanceSummaryViewModel Summary { get; private set; } = new();

    public int UnassignedChargesCount { get; private set; }

    public int CurrentYear { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        UnassignedChargesCount = await _dbContext.Charges.CountAsync(c => c.MemberId == null, cancellationToken);
        CurrentYear = DateTime.Today.Year;
        var currentYearStart = new DateOnly(CurrentYear, 1, 1);
        var cashSnapshot = await FinanceCashCalculator.CalculateAsync(_dbContext, CurrentYear, cancellationToken);

        var plots = await _dbContext.Plots
            .AsNoTracking()
            .Select(plot => new
            {
                PlotId = plot.Id,
                PlotNumber = plot.Number,
                Address = plot.Address
            })
            .ToListAsync(cancellationToken);

        var activeChargeAmountsByPlot = await _dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.CancelledAtUtc == null && charge.PlotId.HasValue)
            .Select(charge => new
            {
                PlotId = charge.PlotId!.Value,
                charge.Amount
            })
            .ToListAsync(cancellationToken);

        var activeChargesByPlotId = activeChargeAmountsByPlot
            .GroupBy(charge => charge.PlotId)
            .ToDictionary(group => group.Key, group => group.Sum(charge => charge.Amount));

        var activePaymentsByPlotId = await _dbContext.LoadActivePaymentTotalsByPlotAsync(plots.Select(p => p.PlotId), cancellationToken);

        var allPlotBalances = plots
            .Select(plot => new PlotBalanceQueryItem
            {
                PlotId = plot.PlotId,
                PlotNumber = plot.PlotNumber,
                Address = plot.Address,
                Charges = activeChargesByPlotId.GetValueOrDefault(plot.PlotId),
                Payments = activePaymentsByPlotId.GetValueOrDefault(plot.PlotId)
            })
            .ToList();

        var activeCharges = await _dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.CancelledAtUtc == null)
            .Select(charge => new
            {
                charge.Id, charge.MemberId,
                charge.Amount,
                charge.ChargeDate
            })
            .ToListAsync(cancellationToken);

        var activePayments = await _dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.CancelledAtUtc == null)
            .Select(payment => new
            {
                payment.Id, payment.MemberId,
                payment.Amount,
                payment.PaymentDate,
                payment.PaymentMethod
            })
            .ToListAsync(cancellationToken);

        var activeExpenses = await _dbContext.Expenses
            .AsNoTracking()
            .Where(expense => !expense.IsCancelled)
            .Select(expense => new
            {
                expense.Amount,
                expense.ExpenseDate
            })
            .ToListAsync(cancellationToken);

        var totalActiveCharges = activeCharges.Sum(charge => charge.Amount);
        var totalActivePayments = activePayments.Sum(payment => payment.Amount);
        var openingYearCharges = activeCharges
            .Where(charge => charge.ChargeDate < currentYearStart)
            .Sum(charge => charge.Amount);
        var openingYearPayments = activePayments
            .Where(payment => payment.PaymentDate < currentYearStart)
            .Sum(payment => payment.Amount);
        var currentYearCharges = activeCharges
            .Where(charge => charge.ChargeDate >= currentYearStart)
            .Sum(charge => charge.Amount);
        var currentYearPayments = activePayments
            .Where(payment => payment.PaymentDate >= currentYearStart)
            .Sum(payment => payment.Amount);

        decimal DebtAt(DateOnly? before) => activeCharges
            .Where(c => !before.HasValue || c.ChargeDate < before.Value)
            .Select(c => (Key: c.MemberId.HasValue ? $"member:{c.MemberId}" : $"charge:{c.Id}", Amount: c.Amount))
            .Concat(activePayments.Where(p => !before.HasValue || p.PaymentDate < before.Value)
                .Select(p => (Key: p.MemberId.HasValue ? $"member:{p.MemberId}" : $"payment:{p.Id}", Amount: -p.Amount)))
            .GroupBy(x => x.Key).Sum(g => Math.Max(g.Sum(x => x.Amount), 0m));
        var balances = activeCharges
            .Select(c => (Key: c.MemberId.HasValue ? $"member:{c.MemberId}" : $"charge:{c.Id}", Amount: c.Amount))
            .Concat(activePayments.Select(p => (Key: p.MemberId.HasValue ? $"member:{p.MemberId}" : $"payment:{p.Id}", Amount: -p.Amount)))
            .GroupBy(x => x.Key).Select(g => g.Sum(x => x.Amount)).ToArray();

        Summary = new FinanceSummaryViewModel
        {
            TotalActiveCharges = totalActiveCharges,
            TotalActivePayments = totalActivePayments,
            CurrentCashAmount = cashSnapshot.CurrentCashAmount,
            CurrentCashOnlyAmount = cashSnapshot.CurrentCashOnlyAmount,
            CurrentNonCashAmount = cashSnapshot.CurrentNonCashAmount,
            OpeningYearCashAmount = cashSnapshot.OpeningYearCashAmount,
            CurrentYearCharges = currentYearCharges,
            OpeningYearDebt = DebtAt(currentYearStart),
            CurrentYearDebt = DebtAt(null),
            PlotsWithDebtCount = allPlotBalances.Count(plot => plot.Balance > 0m),
            PlotsWithOverpaymentCount = allPlotBalances.Count(plot => plot.Balance < 0m),
            PlotsWithZeroBalanceCount = allPlotBalances.Count(plot => plot.Balance == 0m)
        };

        Summary.TotalCurrentDebt = balances.Sum(b => Math.Max(b, 0m));
        Summary.TotalOverpayments = balances.Sum(b => Math.Max(-b, 0m));

    }

    public sealed class FinanceSummaryViewModel
    {
        public decimal TotalActiveCharges { get; set; }

        public decimal TotalActivePayments { get; set; }

        public decimal CurrentCashAmount { get; set; }

        public decimal CurrentCashOnlyAmount { get; set; }

        public decimal CurrentNonCashAmount { get; set; }

        public decimal OpeningYearCashAmount { get; set; }

        public decimal CurrentYearCharges { get; set; }

        public decimal OpeningYearDebt { get; set; }

        public decimal CurrentYearDebt { get; set; }

        public decimal TotalCurrentDebt { get; set; }

        public decimal TotalOverpayments { get; set; }

        public int PlotsWithDebtCount { get; set; }

        public int PlotsWithOverpaymentCount { get; set; }

        public int PlotsWithZeroBalanceCount { get; set; }
    }

    private sealed class PlotBalanceQueryItem
    {
        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public string? Address { get; init; }

        public decimal Charges { get; init; }

        public decimal Payments { get; init; }

        public decimal Balance => Charges - Payments;
    }
}
