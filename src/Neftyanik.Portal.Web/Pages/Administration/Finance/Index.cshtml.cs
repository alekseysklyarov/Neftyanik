using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance;

[Authorize(Roles = RoleNames.Administrator)]
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

    public int TotalPages { get; private set; }

    public string EmptyStateMessage { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
        Status = NormalizeStatus(Status);
        PageNumber = PageNumber < 1 ? 1 : PageNumber;

        var balancesQuery = _dbContext.Plots
            .AsNoTracking()
            .SelectFinanceSummary()
            .Select(plot => new PlotBalanceQueryItem
            {
                PlotId = plot.PlotId,
                PlotNumber = plot.PlotNumber,
                Address = plot.PlotAddress,
                Charges = plot.ActiveChargesTotal,
                Payments = plot.ActivePaymentsTotal
            });

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

        var totalCount = await balancesQuery.CountAsync(cancellationToken);
        TotalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)PageSize);
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        PlotBalances = await balancesQuery
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
            .ToListAsync(cancellationToken);

        Summary = new FinanceSummaryViewModel
        {
            TotalActiveCharges = await _dbContext.Charges.AsNoTracking().Where(charge => charge.CancelledAtUtc == null).SumAsync(charge => (decimal?)charge.Amount, cancellationToken) ?? 0m,
            TotalActivePayments = await _dbContext.Payments.AsNoTracking().Where(payment => payment.CancelledAtUtc == null).SumAsync(payment => (decimal?)payment.Amount, cancellationToken) ?? 0m,
            PlotsWithDebtCount = await _dbContext.Plots.AsNoTracking().CountAsync(plot =>
                (plot.Charges.Where(charge => charge.CancelledAtUtc == null).Sum(charge => (decimal?)charge.Amount) ?? 0m)
                - (plot.Payments.Where(payment => payment.CancelledAtUtc == null).Sum(payment => (decimal?)payment.Amount) ?? 0m) > 0m,
                cancellationToken),
            PlotsWithOverpaymentCount = await _dbContext.Plots.AsNoTracking().CountAsync(plot =>
                (plot.Charges.Where(charge => charge.CancelledAtUtc == null).Sum(charge => (decimal?)charge.Amount) ?? 0m)
                - (plot.Payments.Where(payment => payment.CancelledAtUtc == null).Sum(payment => (decimal?)payment.Amount) ?? 0m) < 0m,
                cancellationToken),
            PlotsWithZeroBalanceCount = await _dbContext.Plots.AsNoTracking().CountAsync(plot =>
                (plot.Charges.Where(charge => charge.CancelledAtUtc == null).Sum(charge => (decimal?)charge.Amount) ?? 0m)
                - (plot.Payments.Where(payment => payment.CancelledAtUtc == null).Sum(payment => (decimal?)payment.Amount) ?? 0m) == 0m,
                cancellationToken)
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
    }
}
