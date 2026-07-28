using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Web.Pages.Finance;

namespace Neftyanik.Portal.Web.Pages.Member;

[Authorize(Roles = RoleNames.Member + "," + RoleNames.Administrator)]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    public MemberDashboardViewModel Dashboard { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public int ChargePage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PaymentPage { get; set; } = 1;

    public IReadOnlyList<PlotViewModel> Plots { get; private set; } = [];

    public IReadOnlyList<ChargeItemViewModel> Charges { get; private set; } = [];

    public IReadOnlyList<PaymentItemViewModel> Payments { get; private set; } = [];

    public IReadOnlyList<MemberElectricityMeterItemViewModel> ElectricityMeters { get; private set; } = [];

    public int ChargeTotalPages { get; private set; } = 1;

    public int PaymentTotalPages { get; private set; } = 1;

    public bool HasChargePreviousPage => ChargePage > 1;

    public bool HasChargeNextPage => ChargePage < ChargeTotalPages;

    public bool HasPaymentPreviousPage => PaymentPage > 1;

    public bool HasPaymentNextPage => PaymentPage < PaymentTotalPages;

    public bool HasElectricityMeters => ElectricityMeters.Count > 0;

    public bool CanSubmitElectricityReading => ElectricityMeters.Count(meter => meter.IsActive && meter.HasInitialReading) > 0;

    public int? SingleReadyElectricityMeterId => ElectricityMeters.Count(meter => meter.IsActive && meter.HasInitialReading) == 1
        ? ElectricityMeters.First(meter => meter.IsActive && meter.HasInitialReading).Id
        : null;

    public bool IsElectricityFeatureAvailable { get; private set; } = true;

    public string? ElectricityFeatureWarningMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
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

        var member = await _dbContext.Members
            .AsNoTracking()
            .Where(item => item.ApplicationUserId == user.Id)
            .Select(item => new MemberDashboardQueryModel
            {
                MemberId = item.Id,
                FullName = item.FullName,
                Email = item.Email,
                PhoneNumber = item.PhoneNumber,
                IsLinked = true
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            Dashboard = new MemberDashboardViewModel
            {
                FullName = !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : user.Email ?? user.UserName ?? "Пользователь",
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                IsLinked = false
            };

            return Page();
        }

        var plots = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(member.MemberId, currentDate)
            .OrderBy(ownership => ownership.Plot != null ? ownership.Plot.Number : string.Empty)
            .Select(ownership => new PlotViewModel
            {
                PlotId = ownership.PlotId,
                PlotNumber = ownership.Plot != null ? ownership.Plot.Number : "—",
                Address = ownership.Plot != null ? ownership.Plot.Address : null,
                OwnershipShare = ownership.OwnershipShare
            })
            .ToListAsync(cancellationToken);

        var plotIds = plots.Select(plot => plot.PlotId).Distinct().ToArray();
        var chargeTotalsByPlot = plotIds.Length == 0
            ? new Dictionary<int, decimal>()
            : (await _dbContext.Charges
                .AsNoTracking()
                .Where(charge => charge.CancelledAtUtc == null
                    && charge.PlotId.HasValue
                    && plotIds.Contains(charge.PlotId.Value))
                .Select(charge => new
                {
                    PlotId = charge.PlotId!.Value,
                    charge.Amount
                })
                .ToListAsync(cancellationToken))
                .GroupBy(item => item.PlotId)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

        var paymentTotalsByPlot = await LoadPaymentTotalsByPlotAsync(plotIds, cancellationToken);

        member.Plots = plots
            .Select(plot => plot with
            {
                ActiveChargesTotal = chargeTotalsByPlot.GetValueOrDefault(plot.PlotId),
                ActivePaymentsTotal = paymentTotalsByPlot.GetValueOrDefault(plot.PlotId)
            })
            .ToList();

        Plots = member.Plots;

        var totalCharges = Plots.Sum(plot => plot.ActiveChargesTotal);
        var totalPayments = await _dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.PlotId != null && plotIds.Contains(payment.PlotId.Value) && payment.CancelledAtUtc == null)
            .Select(payment => payment.Amount)
            .ToListAsync(cancellationToken);

        var chargesQuery = _dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.PlotId != null && plotIds.Contains(charge.PlotId.Value))
            .OrderByDescending(charge => charge.ChargeDate)
            .ThenByDescending(charge => charge.Id);

        var chargeCount = await chargesQuery.CountAsync(cancellationToken);
        ChargeTotalPages = chargeCount == 0 ? 1 : (int)Math.Ceiling(chargeCount / 10d);
        if (ChargePage > ChargeTotalPages)
        {
            ChargePage = ChargeTotalPages;
        }

        Charges = await chargesQuery
            .Skip((ChargePage - 1) * 10)
            .Take(10)
            .Select(charge => new ChargeItemViewModel
            {
                PlotId = charge.PlotId!.Value,
                PlotNumber = charge.Plot != null ? charge.Plot.Number : "—",
                ChargeDate = charge.ChargeDate,
                ChargeTypeName = charge.ChargeType != null ? charge.ChargeType.Name : "—",
                Amount = charge.Amount,
                DueDate = charge.DueDate,
                Description = charge.Description,
                IsCancelled = charge.CancelledAtUtc != null,
                CancellationReason = charge.CancellationReason
            })
            .ToListAsync(cancellationToken);

        var paymentsQuery = _dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.PlotId != null && plotIds.Contains(payment.PlotId.Value))
            .OrderByDescending(payment => payment.PaymentDate)
            .ThenByDescending(payment => payment.Id);

        var paymentCount = await paymentsQuery.CountAsync(cancellationToken);
        PaymentTotalPages = paymentCount == 0 ? 1 : (int)Math.Ceiling(paymentCount / 10d);
        if (PaymentPage > PaymentTotalPages)
        {
            PaymentPage = PaymentTotalPages;
        }

        Payments = await paymentsQuery
            .Skip((PaymentPage - 1) * 10)
            .Take(10)
            .Select(payment => new PaymentItemViewModel
            {
                PlotId = payment.PlotId!.Value,
                PlotNumber = payment.Plot != null ? payment.Plot.Number : "—",
                PaymentDate = payment.PaymentDate,
                Amount = payment.Amount,
                PaymentMethodText = FinanceDisplayHelper.GetPaymentMethodText(payment.PaymentMethod),
                ReferenceNumber = payment.ReferenceNumber,
                Description = payment.Description,
                IsCancelled = payment.CancelledAtUtc != null,
                CancellationReason = payment.CancellationReason
            })
            .ToListAsync(cancellationToken);

        await LoadElectricityStateAsync(member.MemberId, cancellationToken);

        Dashboard = new MemberDashboardViewModel
        {
            MemberId = member.MemberId,
            FullName = member.FullName,
            Email = member.Email,
            PhoneNumber = member.PhoneNumber,
            IsLinked = member.IsLinked,
            IsActive = true,
            ActivePlotsCount = Plots.Count,
            TotalCharges = totalCharges,
            TotalPayments = totalPayments.Sum(),
            Plots = member.Plots
        };

        return Page();
    }

    private sealed class MemberDashboardQueryModel
    {
        public int MemberId { get; init; }

        public string FullName { get; init; } = string.Empty;

        public string? Email { get; init; }

        public string? PhoneNumber { get; init; }

        public bool IsLinked { get; init; }

        public IReadOnlyList<PlotViewModel> Plots { get; set; } = [];
    }

    private async Task<Dictionary<int, decimal>> LoadPaymentTotalsByPlotAsync(int[] plotIds, CancellationToken cancellationToken)
    {
        if (plotIds.Length == 0)
        {
            return [];
        }

        var charges = await _dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.CancelledAtUtc == null
                && charge.PlotId.HasValue
                && plotIds.Contains(charge.PlotId.Value))
            .Select(charge => new
            {
                charge.Id,
                PlotId = charge.PlotId!.Value
            })
            .ToListAsync(cancellationToken);

        if (charges.Count == 0)
        {
            return [];
        }

        var chargeIds = charges.Select(charge => charge.Id).ToArray();
        var allocations = await _dbContext.PaymentAllocations
            .AsNoTracking()
            .Where(allocation => chargeIds.Contains(allocation.ChargeId)
                && allocation.Payment != null
                && allocation.Payment.CancelledAtUtc == null)
            .Select(allocation => new
            {
                allocation.ChargeId,
                allocation.Amount
            })
            .ToListAsync(cancellationToken);

        var plotIdsByCharge = charges.ToDictionary(charge => charge.Id, charge => charge.PlotId);
        return allocations
            .GroupBy(allocation => plotIdsByCharge[allocation.ChargeId])
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));
    }

    private async Task LoadElectricityStateAsync(int memberId, CancellationToken cancellationToken)
    {
        try
        {
            var meters = await _dbContext.MemberElectricityMeters
                .AsNoTracking()
                .Where(meter => meter.MemberId == memberId)
                .OrderBy(meter => meter.Name)
                .ThenBy(meter => meter.MeterNumber)
                .Select(meter => new MemberElectricityMeterItemViewModel
                {
                    Id = meter.Id,
                    Name = meter.Name,
                    MeterNumber = meter.MeterNumber,
                    BillingPlotNumber = meter.BillingPlot != null ? meter.BillingPlot.Number : "—",
                    IsActive = meter.IsActive,
                    HasReadings = meter.Readings.Any(),
                    HasInitialReading = meter.Readings.Any(reading => reading.IsInitialReading),
                    LatestReadingDate = meter.Readings.OrderByDescending(reading => reading.ReadingDate).ThenByDescending(reading => reading.Id).Select(reading => (DateOnly?)reading.ReadingDate).FirstOrDefault(),
                    LatestReading = meter.Readings.OrderByDescending(reading => reading.ReadingDate).ThenByDescending(reading => reading.Id).Select(reading => (decimal?)reading.CurrentReading).FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            var meterIds = meters.Select(meter => meter.Id).ToArray();
            var readingsByMeterId = meterIds.Length == 0
                ? new Dictionary<int, List<MemberElectricityReadingHistoryItemViewModel>>()
                : (await _dbContext.MemberElectricityReadings
                    .AsNoTracking()
                    .Where(reading => meterIds.Contains(reading.MemberElectricityMeterId))
                    .OrderByDescending(reading => reading.ReadingDate)
                    .ThenByDescending(reading => reading.Id)
                    .Select(reading => new
                    {
                        reading.MemberElectricityMeterId,
                        Item = new MemberElectricityReadingHistoryItemViewModel
                        {
                            ReadingDate = reading.ReadingDate,
                            CurrentReading = reading.CurrentReading,
                            Consumption = reading.Consumption,
                            Amount = reading.Amount,
                            IsInitialReading = reading.IsInitialReading
                        }
                    })
                    .ToListAsync(cancellationToken))
                    .GroupBy(item => item.MemberElectricityMeterId)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(item => item.Item).ToList());

            ElectricityMeters = meters
                .Select(meter =>
                {
                    var readings = readingsByMeterId.GetValueOrDefault(meter.Id, new List<MemberElectricityReadingHistoryItemViewModel>());

                    return meter with
                    {
                        Readings = readings
                    };
                })
                .ToList();

            IsElectricityFeatureAvailable = true;
            ElectricityFeatureWarningMessage = null;
        }
        catch (SqlException exception) when (IsMissingTableException(exception))
        {
            ElectricityMeters = [];
            IsElectricityFeatureAvailable = false;
            ElectricityFeatureWarningMessage = "Модуль электросчётчиков недоступен: необходимо применить обновление базы данных.";
        }
    }

    private static bool IsMissingTableException(SqlException exception)
    {
        return exception.Number == 208
            || exception.Message.Contains("MemberElectricityMeters", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class MemberDashboardViewModel
    {
        public int MemberId { get; init; }

        public string FullName { get; init; } = string.Empty;

        public string? Email { get; init; }

        public string? PhoneNumber { get; init; }

        public bool IsLinked { get; init; }

        public bool IsActive { get; init; }

        public int ActivePlotsCount { get; init; }

        public decimal TotalCharges { get; init; }

        public decimal TotalPayments { get; init; }

        public decimal Balance => TotalCharges - TotalPayments;

        public decimal BalanceDisplayAmount => Math.Abs(Balance);

        public string BalanceStatusText => FinanceDisplayHelper.GetBalanceStatusText(Balance);

        public string BalanceStatusClass => FinanceDisplayHelper.GetBalanceStatusClass(Balance);

        public string BalanceCardClass => Balance switch
        {
            > 0m => "border-danger",
            < 0m => "border-primary",
            _ => "border-success"
        };

        public IReadOnlyList<PlotViewModel> Plots { get; set; } = [];
    }

    public sealed record PlotViewModel
    {
        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public string? Address { get; init; }

        public decimal? OwnershipShare { get; init; }

        public decimal ActiveChargesTotal { get; init; }

        public decimal ActivePaymentsTotal { get; init; }

        public decimal Balance => ActiveChargesTotal - ActivePaymentsTotal;

        public decimal BalanceDisplayAmount => Math.Abs(Balance);

        public string Status => FinanceDisplayHelper.GetBalanceStatusText(Balance);

        public string BalanceStatusClass => FinanceDisplayHelper.GetBalanceStatusClass(Balance);
    }

    public sealed class ChargeItemViewModel
    {
        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

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
        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public DateOnly PaymentDate { get; init; }

        public decimal Amount { get; init; }

        public string PaymentMethodText { get; init; } = string.Empty;

        public string? ReferenceNumber { get; init; }

        public string? Description { get; init; }

        public bool IsCancelled { get; init; }

        public string? CancellationReason { get; init; }

        public string StatusText => IsCancelled ? "Отменено" : "Активно";
    }

    public sealed record MemberElectricityMeterItemViewModel
    {
        public int Id { get; init; }

        public string? Name { get; init; }

        public string? MeterNumber { get; init; }

        public string BillingPlotNumber { get; init; } = "—";

        public bool IsActive { get; init; }

        public bool HasReadings { get; init; }

        public bool HasInitialReading { get; init; }

        public DateOnly? LatestReadingDate { get; init; }

        public decimal? LatestReading { get; init; }

        public IReadOnlyList<MemberElectricityReadingHistoryItemViewModel> Readings { get; init; } = [];

        public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : !string.IsNullOrWhiteSpace(MeterNumber) ? MeterNumber : $"Счётчик #{Id}";
    }

    public sealed class MemberElectricityReadingHistoryItemViewModel
    {
        public DateOnly ReadingDate { get; init; }

        public decimal CurrentReading { get; init; }

        public decimal? Consumption { get; init; }

        public decimal? Amount { get; init; }

        public bool IsInitialReading { get; init; }
    }
}
