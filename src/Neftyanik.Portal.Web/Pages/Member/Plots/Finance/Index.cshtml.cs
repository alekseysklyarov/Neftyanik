using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Web.Localization;
using Neftyanik.Portal.Web.Pages.Finance;

namespace Neftyanik.Portal.Web.Pages.Member.Plots.Finance;

[Authorize(Roles = RoleNames.Member)]
public class IndexModel : PageModel
{
    private const int PageSize = 10;
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public int ChargePage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PaymentPage { get; set; } = 1;

    public PlotFinanceViewModel Plot { get; private set; } = new();

    public IReadOnlyList<ChargeItemViewModel> Charges { get; private set; } = [];

    public IReadOnlyList<PaymentItemViewModel> Payments { get; private set; } = [];

    public int ChargeTotalPages { get; private set; } = 1;

    public int PaymentTotalPages { get; private set; } = 1;

    public bool HasChargePreviousPage => ChargePage > 1;

    public bool HasChargeNextPage => ChargePage < ChargeTotalPages;

    public bool HasPaymentPreviousPage => PaymentPage > 1;

    public bool HasPaymentNextPage => PaymentPage < PaymentTotalPages;

    public async Task<IActionResult> OnGetAsync(int plotId, CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);

        ChargePage = ChargePage < 1 ? 1 : ChargePage;
        PaymentPage = PaymentPage < 1 ? 1 : PaymentPage;

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (user.MustChangePassword)
        {
            return RedirectToPage("/Account/ChangeInitialPassword");
        }

        var memberId = await _dbContext.Members
            .AsNoTracking()
            .Where(member => member.ApplicationUserId == user.Id)
            .Select(member => (int?)member.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!memberId.HasValue)
        {
            return NotFound();
        }

        var ownership = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(memberId.Value, currentDate)
            .Where(ownershipItem => ownershipItem.PlotId == plotId)
            .Select(ownershipItem => new PlotOwnershipViewModel
            {
                PlotId = ownershipItem.PlotId,
                OwnershipShare = ownershipItem.OwnershipShare
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (ownership is null)
        {
            return NotFound();
        }

        var plotInfo = await _dbContext.Plots
            .AsNoTracking()
            .Where(plot => plot.Id == plotId)
            .Select(plot => new
            {
                PlotId = plot.Id,
                PlotNumber = plot.Number,
                PlotAddress = plot.Address
            })
            .FirstAsync(cancellationToken);

        var activeChargeAmounts = await _dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.PlotId == plotId && charge.CancelledAtUtc == null)
            .Select(charge => charge.Amount)
            .ToListAsync(cancellationToken);

        var paymentTotalsByPlot = await _dbContext.LoadActivePaymentTotalsByPlotAsync([plotId], memberId.Value, cancellationToken);

        Plot = new PlotFinanceViewModel
        {
            PlotId = plotInfo.PlotId,
            PlotNumber = plotInfo.PlotNumber,
            PlotAddress = plotInfo.PlotAddress,
            OwnershipShare = ownership.OwnershipShare,
            ActiveChargesTotal = activeChargeAmounts.Sum(),
            ActivePaymentsTotal = paymentTotalsByPlot.GetValueOrDefault(plotId)
        };

            var chargesQuery = _dbContext.Charges
                .AsNoTracking()
                .Where(charge => charge.PlotId == plotId)
                .OrderByDescending(charge => charge.ChargeDate)
                .ThenByDescending(charge => charge.Id);

            var chargeCount = await chargesQuery.CountAsync(cancellationToken);
            ChargeTotalPages = chargeCount == 0 ? 1 : (int)Math.Ceiling(chargeCount / (double)PageSize);
            if (ChargePage > ChargeTotalPages)
            {
                ChargePage = ChargeTotalPages;
            }

            Charges = (await chargesQuery
                .Skip((ChargePage - 1) * PageSize)
                .Take(PageSize)
                .Select(charge => new
                {
                    charge.Id,
                    charge.ChargeDate,
                    ChargeTypeName = charge.ChargeType != null ? charge.ChargeType.Name : "—",
                    charge.Amount,
                    charge.DueDate,
                    charge.Description,
                    IsCancelled = charge.CancelledAtUtc != null,
                    charge.CancelledAtUtc,
                    charge.CancellationReason
                })
                .ToListAsync(cancellationToken))
                .Select(charge => new ChargeItemViewModel
                {
                    ChargeId = charge.Id,
                    ChargeDate = charge.ChargeDate,
                    ChargeTypeName = charge.ChargeTypeName,
                    Amount = charge.Amount,
                    DueDate = charge.DueDate,
                    Description = charge.Description,
                    IsCancelled = charge.IsCancelled,
                    CancelledAtUtc = charge.CancelledAtUtc,
                    CancellationReason = charge.CancellationReason
                })
                .ToList();

            var paymentsQuery = _dbContext.Payments
                .AsNoTracking()
                .Where(payment => payment.MemberId == memberId.Value && payment.PlotId == plotId)
                .OrderByDescending(payment => payment.PaymentDate)
                .ThenByDescending(payment => payment.Id);

            var paymentCount = await paymentsQuery.CountAsync(cancellationToken);
            PaymentTotalPages = paymentCount == 0 ? 1 : (int)Math.Ceiling(paymentCount / (double)PageSize);
            if (PaymentPage > PaymentTotalPages)
            {
                PaymentPage = PaymentTotalPages;
            }

            Payments = (await paymentsQuery
                .Skip((PaymentPage - 1) * PageSize)
                .Take(PageSize)
                .Select(payment => new
                {
                    payment.Id,
                    payment.PaymentDate,
                    payment.Amount,
                    payment.PaymentMethod,
                    payment.ReferenceNumber,
                    payment.Description,
                    IsCancelled = payment.CancelledAtUtc != null,
                    payment.CancelledAtUtc,
                    payment.CancellationReason
                })
                .ToListAsync(cancellationToken))
                .Select(payment => new PaymentItemViewModel
                {
                    PaymentId = payment.Id,
                    PaymentDate = payment.PaymentDate,
                    Amount = payment.Amount,
                    PaymentMethodText = FinanceDisplayHelper.GetPaymentMethodText(payment.PaymentMethod),
                    ReferenceNumber = payment.ReferenceNumber,
                    Description = payment.Description,
                    IsCancelled = payment.IsCancelled,
                    CancelledAtUtc = payment.CancelledAtUtc,
                    CancellationReason = payment.CancellationReason
                })
                .ToList();

        return Page();
    }

    private sealed class PlotOwnershipViewModel
    {
        public int PlotId { get; init; }

        public decimal? OwnershipShare { get; init; }
    }

    public sealed class PlotFinanceViewModel
    {
        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public string? PlotAddress { get; init; }

        public decimal? OwnershipShare { get; init; }

        public decimal ActiveChargesTotal { get; init; }

        public decimal ActivePaymentsTotal { get; init; }

        public decimal Balance => ActiveChargesTotal - ActivePaymentsTotal;

        public decimal BalanceDisplayAmount => Math.Abs(Balance);

        public string BalanceStatusText => Balance switch
        {
            > 0m => AppLocalizer.Get("Задолженность", "Заборгованість", "Debt"),
            < 0m => AppLocalizer.Get("Переплата", "Переплата", "Overpayment"),
            _ => AppLocalizer.Get("Оплачено", "Сплачено", "Paid")
        };

        public string BalanceStatusClass => Balance switch
        {
            > 0m => "text-danger",
            < 0m => "text-primary",
            _ => "text-success"
        };

        public string BalanceCardClass => Balance switch
        {
            > 0m => "border-danger",
            < 0m => "border-primary",
            _ => "border-success"
        };
    }

    public sealed class ChargeItemViewModel
    {
        public long ChargeId { get; init; }

        public DateOnly ChargeDate { get; init; }

        public string ChargeTypeName { get; init; } = string.Empty;

        public decimal Amount { get; init; }

        public DateOnly? DueDate { get; init; }

        public string? Description { get; init; }

        public bool IsCancelled { get; init; }

        public DateTime? CancelledAtUtc { get; init; }

        public string? CancellationReason { get; init; }

        public string StatusText => IsCancelled
            ? AppLocalizer.Get("Отменено", "Скасовано", "Cancelled")
            : AppLocalizer.Get("Активно", "Активно", "Active");
    }

    public sealed class PaymentItemViewModel
    {
        public long PaymentId { get; init; }

        public DateOnly PaymentDate { get; init; }

        public decimal Amount { get; init; }

        public string PaymentMethodText { get; init; } = string.Empty;

        public string? ReferenceNumber { get; init; }

        public string? Description { get; init; }

        public bool IsCancelled { get; init; }

        public DateTime? CancelledAtUtc { get; init; }

        public string? CancellationReason { get; init; }

        public string StatusText => IsCancelled
            ? AppLocalizer.Get("Отменено", "Скасовано", "Cancelled")
            : AppLocalizer.Get("Активно", "Активно", "Active");
    }
}
