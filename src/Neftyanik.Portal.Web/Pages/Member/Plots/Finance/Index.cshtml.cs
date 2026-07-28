using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
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

    public ElectricitySummaryViewModel Electricity { get; private set; } = new();

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

        var chargeIds = await _dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.PlotId == plotId && charge.CancelledAtUtc == null)
            .Select(charge => charge.Id)
            .ToArrayAsync(cancellationToken);

        var activePaymentAmounts = chargeIds.Length == 0
            ? []
            : await _dbContext.PaymentAllocations
                .AsNoTracking()
                .Where(allocation => chargeIds.Contains(allocation.ChargeId)
                    && allocation.Payment != null
                    && allocation.Payment.CancelledAtUtc == null)
                .Select(allocation => allocation.Amount)
                .ToListAsync(cancellationToken);

        Plot = new PlotFinanceViewModel
        {
            PlotId = plotInfo.PlotId,
            PlotNumber = plotInfo.PlotNumber,
            PlotAddress = plotInfo.PlotAddress,
            OwnershipShare = ownership.OwnershipShare,
            ActiveChargesTotal = activeChargeAmounts.Sum(),
            ActivePaymentsTotal = activePaymentAmounts.Sum()
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
                    charge.ChargeDate,
                    ChargeTypeName = charge.ChargeType != null ? charge.ChargeType.Name : "—",
                    charge.Amount,
                    charge.DueDate,
                    charge.Description,
                    IsCancelled = charge.CancelledAtUtc != null,
                    charge.CancellationReason
                })
                .ToListAsync(cancellationToken))
                .Select(charge => new ChargeItemViewModel
                {
                    ChargeDate = charge.ChargeDate,
                    ChargeTypeName = charge.ChargeTypeName,
                    Amount = charge.Amount,
                    DueDate = charge.DueDate,
                    Description = charge.Description,
                    IsCancelled = charge.IsCancelled,
                    CancellationReason = charge.CancellationReason
                })
                .ToList();

            var paymentsQuery = _dbContext.Payments
                .AsNoTracking()
                .Where(payment => payment.PlotId == plotId)
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
                    payment.PaymentDate,
                    payment.Amount,
                    payment.PaymentMethod,
                    payment.ReferenceNumber,
                    payment.Description,
                    IsCancelled = payment.CancelledAtUtc != null,
                    payment.CancellationReason
                })
                .ToListAsync(cancellationToken))
                .Select(payment => new PaymentItemViewModel
                {
                    PaymentDate = payment.PaymentDate,
                    Amount = payment.Amount,
                    PaymentMethodText = FinanceDisplayHelper.GetPaymentMethodText(payment.PaymentMethod),
                    ReferenceNumber = payment.ReferenceNumber,
                    Description = payment.Description,
                    IsCancelled = payment.IsCancelled,
                    CancellationReason = payment.CancellationReason
                })
                .ToList();

            Electricity = new ElectricitySummaryViewModel();

            var hasElectricityHistory = await _dbContext.ElectricityReadings
                .AsNoTracking()
                .AnyAsync(reading => reading.PlotId == plotId, cancellationToken);

            if (hasElectricityHistory)
            {
                var latestReading = await _dbContext.ElectricityReadings
                    .AsNoTracking()
                    .Where(reading => reading.PlotId == plotId)
                    .OrderByDescending(reading => reading.ReadingDate)
                    .ThenByDescending(reading => reading.Id)
                    .Select(reading => new
                    {
                        reading.ReadingDate,
                        reading.CurrentDayReading,
                        reading.CurrentNightReading
                    })
                    .FirstAsync(cancellationToken);

                Electricity = new ElectricitySummaryViewModel
                {
                    HasHistory = true,
                    LatestReadingDate = latestReading.ReadingDate,
                    LatestDayReading = latestReading.CurrentDayReading,
                    LatestNightReading = latestReading.CurrentNightReading
                };

                var recentReadings = await _dbContext.ElectricityReadings
                    .AsNoTracking()
                    .Where(reading => reading.PlotId == plotId)
                    .OrderByDescending(reading => reading.ReadingDate)
                    .ThenByDescending(reading => reading.Id)
                    .Take(5)
                    .Select(reading => new
                    {
                        reading.ReadingDate,
                        reading.IsInitialReading,
                        reading.DayConsumption,
                        reading.NightConsumption,
                        reading.DayRate,
                        reading.NightRate,
                        reading.TotalAmount,
                        reading.ChargeId
                    })
                    .ToListAsync(cancellationToken);

                var cancelledChargeIds = recentReadings
                    .Where(reading => reading.ChargeId.HasValue)
                    .Select(reading => reading.ChargeId!.Value)
                    .ToArray();

                HashSet<long> cancelledChargeIdSet = [];
                if (cancelledChargeIds.Length > 0)
                {
                    cancelledChargeIdSet = (await _dbContext.Charges
                        .AsNoTracking()
                        .Where(charge => cancelledChargeIds.Contains(charge.Id) && charge.CancelledAtUtc != null)
                        .Select(charge => charge.Id)
                        .ToListAsync(cancellationToken))
                        .ToHashSet();
                }

                Electricity.RecentReadings = recentReadings
                    .Select(reading => new ElectricityReadingItemViewModel
                    {
                        ReadingDate = reading.ReadingDate,
                        IsInitialReading = reading.IsInitialReading,
                        DayConsumption = reading.DayConsumption,
                        NightConsumption = reading.NightConsumption,
                        DayRate = reading.DayRate,
                        NightRate = reading.NightRate,
                        TotalAmount = reading.TotalAmount,
                        ChargeId = reading.ChargeId,
                        IsChargeCancelled = reading.ChargeId.HasValue && cancelledChargeIdSet.Contains(reading.ChargeId.Value)
                    })
                    .ToList();
            }

        return Page();
    }

    public sealed class ElectricitySummaryViewModel
    {
        public bool HasHistory { get; init; }

        public DateOnly? LatestReadingDate { get; init; }

        public decimal? LatestDayReading { get; init; }

        public decimal? LatestNightReading { get; init; }

        public IReadOnlyList<ElectricityReadingItemViewModel> RecentReadings { get; set; } = [];
    }

    public sealed class ElectricityReadingItemViewModel
    {
        public DateOnly ReadingDate { get; init; }

        public bool IsInitialReading { get; init; }

        public decimal? DayConsumption { get; init; }

        public decimal? NightConsumption { get; init; }

        public decimal? DayRate { get; init; }

        public decimal? NightRate { get; init; }

        public decimal? TotalAmount { get; init; }

        public long? ChargeId { get; init; }

        public bool IsChargeCancelled { get; set; }

        public string ChargeStatusText => IsInitialReading
            ? "Без начисления"
            : IsChargeCancelled ? "Начисление отменено" : "Начислено";
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
            > 0m => "Задолженность",
            < 0m => "Переплата",
            _ => "Оплачено"
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
        public DateOnly ChargeDate { get; init; }

        public string ChargeTypeName { get; init; } = string.Empty;

        public decimal Amount { get; init; }

        public DateOnly? DueDate { get; init; }

        public string? Description { get; init; }

        public bool IsCancelled { get; init; }

        public string? CancellationReason { get; init; }

        public string StatusText => IsCancelled ? "Отменено" : "Активно";
    }

    public sealed class PaymentItemViewModel
    {
        public DateOnly PaymentDate { get; init; }

        public decimal Amount { get; init; }

        public string PaymentMethodText { get; init; } = string.Empty;

        public string? ReferenceNumber { get; init; }

        public string? Description { get; init; }

        public bool IsCancelled { get; init; }

        public string? CancellationReason { get; init; }

        public string StatusText => IsCancelled ? "Отменено" : "Активно";
    }
}
