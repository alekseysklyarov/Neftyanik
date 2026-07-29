using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class IndexModel : PageModel
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<PlotBalanceViewModel> PlotBalances { get; private set; } = [];

    public FinanceSummaryViewModel Summary { get; private set; } = new();

    public int CurrentYear { get; private set; }

    public int TotalPages { get; private set; }

    public string EmptyStateMessage { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
        Status = NormalizeStatus(Status);
        PageNumber = PageNumber < 1 ? 1 : PageNumber;

        CurrentYear = DateTime.Today.Year;
        var currentYearStart = new DateOnly(CurrentYear, 1, 1);

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

        var activePaymentAmountsByPlot = await _dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.CancelledAtUtc == null && payment.PlotId.HasValue)
            .Select(payment => new
            {
                PlotId = payment.PlotId!.Value,
                payment.Amount
            })
            .ToListAsync(cancellationToken);

        var activeChargesByPlotId = activeChargeAmountsByPlot
            .GroupBy(charge => charge.PlotId)
            .ToDictionary(group => group.Key, group => group.Sum(charge => charge.Amount));

        var activePaymentsByPlotId = activePaymentAmountsByPlot
            .GroupBy(payment => payment.PlotId)
            .ToDictionary(group => group.Key, group => group.Sum(payment => payment.Amount));

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

        IEnumerable<PlotBalanceQueryItem> balancesQuery = allPlotBalances;

        if (!string.IsNullOrWhiteSpace(Search))
        {
            balancesQuery = balancesQuery.Where(item => item.PlotNumber.Contains(Search) || (item.Address != null && item.Address.Contains(Search)));
        }

        balancesQuery = Status switch
        {
            "debt" => balancesQuery.Where(item => item.Charges - item.Payments > 0m),
            "nodebt" => balancesQuery.Where(item => item.Charges - item.Payments == 0m),
            "overpayment" => balancesQuery.Where(item => item.Charges - item.Payments < 0m),
            _ => balancesQuery
        };

        var allFilteredBalances = balancesQuery.ToList();
        var totalCount = allFilteredBalances.Count;
        TotalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)PageSize);
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        PlotBalances = allFilteredBalances
            .OrderByDescending(item => item.Charges - item.Payments)
            .ThenBy(item => item.PlotNumber)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(item => new PlotBalanceViewModel
            {
                PlotId = item.PlotId,
                PlotNumber = item.PlotNumber,
                Address = item.Address,
                Charges = item.Charges,
                Payments = item.Payments
            })
            .ToList();

        var activeCharges = await _dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.CancelledAtUtc == null)
            .Select(charge => new
            {
                charge.Amount,
                charge.ChargeDate
            })
            .ToListAsync(cancellationToken);

        var activePayments = await _dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.CancelledAtUtc == null)
            .Select(payment => new
            {
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
        var totalCashPayments = activePayments
            .Where(payment => payment.PaymentMethod == Domain.Enums.PaymentMethod.Cash)
            .Sum(payment => payment.Amount);
        var openingYearCashPayments = activePayments
            .Where(payment => payment.PaymentMethod == Domain.Enums.PaymentMethod.Cash && payment.PaymentDate < currentYearStart)
            .Sum(payment => payment.Amount);
        var totalActiveExpenses = activeExpenses.Sum(expense => expense.Amount);
        var openingYearExpenses = activeExpenses
            .Where(expense => expense.ExpenseDate < currentYearStart)
            .Sum(expense => expense.Amount);
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

        Summary = new FinanceSummaryViewModel
        {
            TotalActiveCharges = totalActiveCharges,
            TotalActivePayments = totalActivePayments,
            CurrentCashAmount = totalCashPayments - totalActiveExpenses,
            OpeningYearCashAmount = openingYearCashPayments - openingYearExpenses,
            CurrentYearCharges = currentYearCharges,
            OpeningYearDebt = Math.Max(openingYearCharges - openingYearPayments, 0m),
            CurrentYearDebt = Math.Max(currentYearCharges - currentYearPayments, 0m),
            PlotsWithDebtCount = allPlotBalances.Count(plot => plot.Balance > 0m),
            PlotsWithOverpaymentCount = allPlotBalances.Count(plot => plot.Balance < 0m),
            PlotsWithZeroBalanceCount = allPlotBalances.Count(plot => plot.Balance == 0m)
        };

        Summary.TotalCurrentDebt = Summary.TotalActiveCharges >= Summary.TotalActivePayments
            ? Summary.TotalActiveCharges - Summary.TotalActivePayments
            : 0m;
        Summary.TotalOverpayments = Summary.TotalActivePayments > Summary.TotalActiveCharges
            ? Summary.TotalActivePayments - Summary.TotalActiveCharges
            : 0m;

        EmptyStateMessage = totalCount == 0
            ? "По выбранным условиям участки с финансовыми данными не найдены."
            : string.Empty;
    }

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    private static string NormalizeStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "debt" => "debt",
            "nodebt" => "nodebt",
            "overpayment" => "overpayment",
            _ => "all"
        };
    }

    public sealed class FinanceSummaryViewModel
    {
        public decimal TotalActiveCharges { get; set; }

        public decimal TotalActivePayments { get; set; }

        public decimal CurrentCashAmount { get; set; }

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

    public sealed class PlotBalanceViewModel
    {
        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public string? Address { get; init; }

        public decimal Charges { get; init; }

        public decimal Payments { get; init; }

        public decimal Balance => Charges - Payments;

        public decimal BalanceDisplayAmount => Math.Abs(Balance);

        public string Status => Balance switch
        {
            > 0m => "Задолженность",
            < 0m => "Переплата",
            _ => "Задолженности нет"
        };
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
