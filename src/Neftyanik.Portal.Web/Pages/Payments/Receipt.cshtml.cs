using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;
using Neftyanik.Portal.Web.Pages.Finance;

namespace Neftyanik.Portal.Web.Pages.Payments;

[Authorize(Roles = RoleNames.Administrator + "," + RoleNames.Accountant + "," + RoleNames.Member)]
public class ReceiptModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public ReceiptModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    public PaymentReceiptViewModel Receipt { get; private set; } = new();

    public bool IsAdministrationView { get; private set; }

    public DateTime GeneratedAtUtc { get; private set; }

    public async Task<IActionResult> OnGetAsync(long paymentId, int? memberId, CancellationToken cancellationToken)
    {
        var isAdministrationMode = memberId.HasValue
            && (User.IsInRole(RoleNames.Administrator) || User.IsInRole(RoleNames.Accountant));

        var payment = await _dbContext.Payments
            .AsNoTracking()
            .Where(item => item.Id == paymentId)
            .Select(item => new
            {
                item.Id,
                item.MemberId,
                item.PlotId,
                item.PaymentDate,
                item.Amount,
                item.BalanceBeforePayment,
                item.BalanceAfterPayment,
                item.PaymentMethod,
                item.ReferenceNumber,
                item.Description,
                item.CreatedAtUtc,
                item.CancelledAtUtc,
                item.CancellationReason,
                MemberFullName = item.Member != null ? item.Member.FullName : null,
                MemberPhoneNumber = item.Member != null ? item.Member.PhoneNumber : null,
                MemberEmail = item.Member != null ? item.Member.Email : null,
                PlotNumber = item.Plot != null ? item.Plot.Number : "—",
                PlotAddress = item.Plot != null ? item.Plot.Address : null,
                CreatedByDisplayName = item.CreatedByUser != null ? item.CreatedByUser.DisplayName : null,
                CreatedByFirstName = item.CreatedByUser != null ? item.CreatedByUser.FirstName : null,
                CreatedByLastName = item.CreatedByUser != null ? item.CreatedByUser.LastName : null,
                CreatedByUserName = item.CreatedByUser != null ? item.CreatedByUser.UserName : null,
                HasPaymentNotification = item.PaymentNotification != null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null || !payment.PlotId.HasValue || !payment.MemberId.HasValue || string.IsNullOrWhiteSpace(payment.MemberFullName))
        {
            return NotFound();
        }

        int resolvedMemberId;
        if (isAdministrationMode)
        {
            if (payment.MemberId.Value != memberId!.Value)
            {
                return NotFound();
            }

            resolvedMemberId = memberId.Value;
            IsAdministrationView = true;
        }
        else if (User.IsInRole(RoleNames.Member))
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            if (user.MustChangePassword)
            {
                return RedirectToPage("/Account/ChangeInitialPassword");
            }

            var currentMemberId = await _dbContext.Members
                .AsNoTracking()
                .Where(item => item.ApplicationUserId == user.Id)
                .Select(item => (int?)item.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (!currentMemberId.HasValue)
            {
                return NotFound();
            }

            if (payment.MemberId.Value != currentMemberId.Value)
            {
                return NotFound();
            }

            resolvedMemberId = currentMemberId.Value;
        }
        else
        {
            return Forbid();
        }

        var allocationItems = await _dbContext.PaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.PaymentId == paymentId)
            .Select(allocation => new PaymentAllocationQueryModel
            {
                ChargeId = allocation.ChargeId,
                ChargeDate = allocation.Charge != null ? allocation.Charge.ChargeDate : null,
                ChargeTypeName = allocation.Charge != null && allocation.Charge.ChargeType != null
                    ? allocation.Charge.ChargeType.Name
                    : "—",
                ChargeDescription = allocation.Charge != null ? allocation.Charge.Description : null,
                ChargeAmount = allocation.Charge != null ? allocation.Charge.Amount : null,
                AllocatedAmount = allocation.Amount,
                IsChargeCancelled = allocation.Charge != null && allocation.Charge.CancelledAtUtc != null,
                MemberElectricityReadingId = allocation.Charge != null && allocation.Charge.MemberElectricityReading != null
                    ? allocation.Charge.MemberElectricityReading.Id
                    : null
            })
            .ToListAsync(cancellationToken);

        var allocations = allocationItems
            .Select(allocation => new PaymentAllocationReceiptViewModel
            {
                ChargeId = allocation.ChargeId,
                ChargeDate = allocation.ChargeDate,
                ChargeTypeName = allocation.ChargeTypeName,
                ChargeDescription = allocation.ChargeDescription,
                ChargeAmount = allocation.ChargeAmount,
                AllocatedAmount = allocation.AllocatedAmount,
                IsChargeCancelled = allocation.IsChargeCancelled
            })
            .OrderBy(item => item.ChargeDate ?? DateOnly.MaxValue)
            .ThenBy(item => item.ChargeId)
            .ToList();

        var electricityReadings = await LoadElectricityReadingsAsync(
            allocationItems.Where(item => item.MemberElectricityReadingId.HasValue)
                .Select(item => item.MemberElectricityReadingId!.Value)
                .Distinct()
                .ToArray(),
            cancellationToken);

        Receipt = new PaymentReceiptViewModel
        {
            PaymentId = payment.Id,
            MemberId = resolvedMemberId,
            MemberFullName = payment.MemberFullName,
            MemberPhoneNumber = payment.MemberPhoneNumber,
            MemberEmail = payment.MemberEmail,
            PlotId = payment.PlotId.Value,
            PlotNumber = payment.PlotNumber,
            PlotAddress = payment.PlotAddress,
            PaymentDate = payment.PaymentDate,
            Amount = payment.Amount,
            BalanceBeforePayment = payment.BalanceBeforePayment,
            BalanceAfterPayment = payment.BalanceAfterPayment,
            PaymentMethodText = FinanceDisplayHelper.GetPaymentMethodText(payment.PaymentMethod),
            ReferenceNumber = payment.ReferenceNumber,
            Description = payment.Description,
            RegisteredAtUtc = payment.CreatedAtUtc,
            RegisteredBy = BuildUserDisplayName(
                payment.CreatedByDisplayName,
                payment.CreatedByFirstName,
                payment.CreatedByLastName,
                payment.CreatedByUserName),
            IsCancelled = payment.CancelledAtUtc.HasValue,
            CancelledAtUtc = payment.CancelledAtUtc,
            CancellationReason = payment.CancellationReason,
            HasPaymentNotification = payment.HasPaymentNotification,
            Allocations = allocations,
            ElectricityReadings = electricityReadings
        };

        GeneratedAtUtc = DateTime.UtcNow;
        return Page();
    }

    private async Task<IReadOnlyList<ElectricityReadingReceiptViewModel>> LoadElectricityReadingsAsync(
        long[] readingIds,
        CancellationToken cancellationToken)
    {
        if (readingIds.Length == 0)
        {
            return [];
        }

        var linkedReadings = await _dbContext.MemberElectricityReadings
            .AsNoTracking()
            .Where(reading => readingIds.Contains(reading.Id))
            .Select(reading => new LinkedElectricityReadingQueryModel
            {
                ReadingId = reading.Id,
                ChargeId = reading.ChargeId,
                MeterId = reading.MemberElectricityMeterId,
                ReadingDate = reading.ReadingDate,
                CurrentReading = reading.CurrentReading,
                CurrentNightReading = reading.CurrentNightReading,
                PlotNumber = reading.Charge != null && reading.Charge.Plot != null ? reading.Charge.Plot.Number : "—",
                MeterName = reading.MemberElectricityMeter != null ? reading.MemberElectricityMeter.Name : null,
                MeterNumber = reading.MemberElectricityMeter != null ? reading.MemberElectricityMeter.MeterNumber : null,
                MeterType = reading.MemberElectricityMeter != null && reading.MemberElectricityMeter.Member != null
                    ? reading.MemberElectricityMeter.Member.ElectricityMeterType
                    : MemberElectricityMeterType.SingleRate
            })
            .OrderBy(reading => reading.ReadingDate)
            .ThenBy(reading => reading.ChargeId)
            .ToListAsync(cancellationToken);

        var meterIds = linkedReadings
            .Select(item => item.MeterId)
            .Distinct()
            .ToArray();

        var meterHistory = await _dbContext.MemberElectricityReadings
            .AsNoTracking()
            .Where(reading => meterIds.Contains(reading.MemberElectricityMeterId))
            .OrderBy(reading => reading.MemberElectricityMeterId)
            .ThenBy(reading => reading.ReadingDate)
            .ThenBy(reading => reading.Id)
            .Select(reading => new MeterReadingHistoryItemQueryModel
            {
                ReadingId = reading.Id,
                MeterId = reading.MemberElectricityMeterId,
                CurrentReading = reading.CurrentReading,
                CurrentNightReading = reading.CurrentNightReading
            })
            .ToListAsync(cancellationToken);

        var previousReadingsById = new Dictionary<long, PreviousMeterReadingQueryModel>();
        foreach (var group in meterHistory.GroupBy(item => item.MeterId))
        {
            decimal? previousReading = null;
            decimal? previousNightReading = null;

            foreach (var reading in group)
            {
                previousReadingsById[reading.ReadingId] = new PreviousMeterReadingQueryModel(previousReading, previousNightReading);
                previousReading = reading.CurrentReading;
                previousNightReading = reading.CurrentNightReading;
            }
        }

        return linkedReadings
            .Select(reading =>
            {
                previousReadingsById.TryGetValue(reading.ReadingId, out var previousReading);

                var dayConsumption = previousReading?.PreviousReading.HasValue == true
                    ? reading.CurrentReading - previousReading.PreviousReading.Value
                    : (decimal?)null;
                decimal? nightConsumption = reading.MeterType == MemberElectricityMeterType.DayNight
                    && previousReading?.PreviousNightReading.HasValue == true
                    && reading.CurrentNightReading.HasValue
                        ? reading.CurrentNightReading.Value - previousReading.PreviousNightReading.Value
                        : (decimal?)null;

                return new ElectricityReadingReceiptViewModel
                {
                    ChargeId = reading.ChargeId ?? 0,
                    PlotNumber = reading.PlotNumber,
                    ReadingDate = reading.ReadingDate,
                    MeterDisplayName = BuildMeterDisplayName(reading.MeterName, reading.MeterNumber, reading.MeterId),
                    MeterType = reading.MeterType,
                    PreviousReading = previousReading?.PreviousReading,
                    CurrentReading = reading.CurrentReading,
                    DayConsumption = dayConsumption,
                    PreviousNightReading = reading.MeterType == MemberElectricityMeterType.DayNight
                        ? previousReading?.PreviousNightReading
                        : null,
                    CurrentNightReading = reading.MeterType == MemberElectricityMeterType.DayNight
                        ? reading.CurrentNightReading
                        : null,
                    NightConsumption = nightConsumption
                };
            })
            .OrderBy(item => item.ReadingDate)
            .ThenBy(item => item.ChargeId)
            .ToList();
    }

    private static string BuildUserDisplayName(string? displayName, string? firstName, string? lastName, string? userName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        var fullName = string.Join(' ', new[] { firstName, lastName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim()));

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName;
        }

        return string.IsNullOrWhiteSpace(userName)
            ? AppLocalizer.Get("Не указан", "Не вказано", "Not specified")
            : userName;
    }

    private static string BuildMeterDisplayName(string? meterName, string? meterNumber, int meterId)
    {
        if (!string.IsNullOrWhiteSpace(meterName))
        {
            return meterName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(meterNumber))
        {
            return meterNumber.Trim();
        }

        return AppLocalizer.Get($"Счётчик #{meterId}", $"Лічильник #{meterId}", $"Meter #{meterId}");
    }

    private static HistoricalBalanceSummaryViewModel BuildHistoricalBalanceSummary(decimal? balance)
    {
        if (!balance.HasValue)
        {
            return new HistoricalBalanceSummaryViewModel(
                false,
                string.Empty,
                AppLocalizer.Get("Исторический баланс недоступен", "Історичний баланс недоступний", "Historical balance unavailable"),
                "text-muted");
        }

        return balance.Value switch
        {
            > 0m => new HistoricalBalanceSummaryViewModel(
                true,
                AppLocalizer.Get("Задолженность", "Заборгованість", "Debt"),
                $"{Math.Abs(balance.Value):0.00} ₴",
                FinanceDisplayHelper.GetBalanceStatusClass(balance.Value)),
            < 0m => new HistoricalBalanceSummaryViewModel(
                true,
                AppLocalizer.Get("Переплата", "Переплата", "Overpayment"),
                $"{Math.Abs(balance.Value):0.00} ₴",
                FinanceDisplayHelper.GetBalanceStatusClass(balance.Value)),
            _ => new HistoricalBalanceSummaryViewModel(
                true,
                AppLocalizer.Get("Задолженности нет", "Заборгованості немає", "No debt"),
                string.Empty,
                FinanceDisplayHelper.GetBalanceStatusClass(balance.Value))
        };
    }

    public sealed class PaymentReceiptViewModel
    {
        public long PaymentId { get; init; }

        public int MemberId { get; init; }

        public string MemberFullName { get; init; } = string.Empty;

        public string? MemberPhoneNumber { get; init; }

        public string? MemberEmail { get; init; }

        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public string? PlotAddress { get; init; }

        public DateOnly PaymentDate { get; init; }

        public decimal Amount { get; init; }

        public decimal? BalanceBeforePayment { get; init; }

        public decimal? BalanceAfterPayment { get; init; }

        public string PaymentMethodText { get; init; } = string.Empty;

        public string? ReferenceNumber { get; init; }

        public string? Description { get; init; }

        public DateTime RegisteredAtUtc { get; init; }

        public string RegisteredBy { get; init; } = string.Empty;

        public bool IsCancelled { get; init; }

        public DateTime? CancelledAtUtc { get; init; }

        public string? CancellationReason { get; init; }

        public bool HasPaymentNotification { get; init; }

        public IReadOnlyList<PaymentAllocationReceiptViewModel> Allocations { get; init; } = [];

        public IReadOnlyList<ElectricityReadingReceiptViewModel> ElectricityReadings { get; init; } = [];

        public decimal AllocatedTotal => Allocations.Sum(item => item.AllocatedAmount);

        public decimal UnallocatedAmount => Math.Max(0m, Amount - AllocatedTotal);

        public HistoricalBalanceSummaryViewModel BalanceBeforeSummary => BuildHistoricalBalanceSummary(BalanceBeforePayment);

        public HistoricalBalanceSummaryViewModel BalanceAfterSummary => BuildHistoricalBalanceSummary(BalanceAfterPayment);

        public string StatusText => IsCancelled
            ? AppLocalizer.Get("Отменён", "Скасований", "Cancelled")
            : AppLocalizer.Get("Зарегистрирован", "Зареєстрований", "Registered");
    }

    public sealed class PaymentAllocationReceiptViewModel
    {
        public long ChargeId { get; init; }

        public DateOnly? ChargeDate { get; init; }

        public string ChargeTypeName { get; init; } = string.Empty;

        public string? ChargeDescription { get; init; }

        public decimal? ChargeAmount { get; init; }

        public decimal AllocatedAmount { get; init; }

        public bool IsChargeCancelled { get; init; }
    }

    public sealed class ElectricityReadingReceiptViewModel
    {
        public long ChargeId { get; init; }

        public string PlotNumber { get; init; } = "—";

        public DateOnly ReadingDate { get; init; }

        public string MeterDisplayName { get; init; } = string.Empty;

        public MemberElectricityMeterType MeterType { get; init; }

        public decimal? PreviousReading { get; init; }

        public decimal CurrentReading { get; init; }

        public decimal? DayConsumption { get; init; }

        public decimal? PreviousNightReading { get; init; }

        public decimal? CurrentNightReading { get; init; }

        public decimal? NightConsumption { get; init; }

        public bool IsDayNight => MeterType == MemberElectricityMeterType.DayNight;

        public string MeterTypeText => IsDayNight
            ? AppLocalizer.Get("День / ночь", "День / ніч", "Day / night")
            : AppLocalizer.Get("Однотарифный", "Однотарифний", "Single-rate");
    }

    public sealed record HistoricalBalanceSummaryViewModel(bool IsAvailable, string Label, string Value, string CssClass);

    private sealed class PaymentAllocationQueryModel
    {
        public long ChargeId { get; init; }

        public DateOnly? ChargeDate { get; init; }

        public string ChargeTypeName { get; init; } = string.Empty;

        public string? ChargeDescription { get; init; }

        public decimal? ChargeAmount { get; init; }

        public decimal AllocatedAmount { get; init; }

        public bool IsChargeCancelled { get; init; }

        public long? MemberElectricityReadingId { get; init; }
    }

    private sealed class LinkedElectricityReadingQueryModel
    {
        public long ReadingId { get; init; }

        public long? ChargeId { get; init; }

        public int MeterId { get; init; }

        public DateOnly ReadingDate { get; init; }

        public decimal CurrentReading { get; init; }

        public decimal? CurrentNightReading { get; init; }

        public string PlotNumber { get; init; } = "—";

        public string? MeterName { get; init; }

        public string? MeterNumber { get; init; }

        public MemberElectricityMeterType MeterType { get; init; }
    }

    private sealed class MeterReadingHistoryItemQueryModel
    {
        public long ReadingId { get; init; }

        public int MeterId { get; init; }

        public decimal CurrentReading { get; init; }

        public decimal? CurrentNightReading { get; init; }
    }

    private sealed record PreviousMeterReadingQueryModel(decimal? PreviousReading, decimal? PreviousNightReading);
}
