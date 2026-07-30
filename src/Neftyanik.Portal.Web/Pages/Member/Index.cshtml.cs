using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Web.Pages.Finance;
using Neftyanik.Portal.Web.Localization;
using Neftyanik.Portal.Web.Security;

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

    [BindProperty]
    public ProfileInputModel Profile { get; set; } = new();

    [BindProperty]
    public ChangePasswordInputModel ChangePassword { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int ChargePage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PaymentPage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int? ChargeTypeId { get; set; }

    public IReadOnlyList<PlotViewModel> Plots { get; private set; } = [];

    public IReadOnlyList<ChargeItemViewModel> Charges { get; private set; } = [];

    public IReadOnlyList<PaymentItemViewModel> Payments { get; private set; } = [];

    public IReadOnlyList<MemberElectricityMeterItemViewModel> ElectricityMeters { get; private set; } = [];

    public IReadOnlyList<SelectListItem> ChargeTypeOptions { get; private set; } = [];

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

    public bool ReopenEditProfileModal { get; private set; }

    public bool ReopenChangePasswordModal { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
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

        await LoadPageStateAsync(user, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateProfileAsync(CancellationToken cancellationToken)
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

        var member = await _dbContext.Members
            .FirstOrDefaultAsync(item => item.ApplicationUserId == user.Id, cancellationToken);
        if (member is null)
        {
            TempData["ErrorMessage"] = AppLocalizer.Get(
                "Учетная запись не связана с карточкой члена товарищества. Обратитесь к администратору.",
                "Обліковий запис не пов'язаний із карткою члена товариства. Зверніться до адміністратора.",
                "The account is not linked to a member record. Contact the administrator.");
            return RedirectToPage();
        }

        Profile.FullName = Profile.FullName.Trim();
        Profile.PhoneNumber = Normalize(Profile.PhoneNumber);
        Profile.Email = Normalize(Profile.Email);

        if (string.IsNullOrWhiteSpace(Profile.FullName))
        {
            ModelState.AddModelError($"{nameof(Profile)}.{nameof(ProfileInputModel.FullName)}", AppLocalizer.Get(
                "Укажите ФИО.",
                "Вкажіть ПІБ.",
                "Enter the full name."));
        }

        if (!string.IsNullOrWhiteSpace(Profile.Email))
        {
            var normalizedEmail = _userManager.NormalizeEmail(Profile.Email);
            var duplicateEmailExists = await _dbContext.Users
                .AsNoTracking()
                .AnyAsync(item => item.Id != user.Id && item.NormalizedEmail == normalizedEmail, cancellationToken);

            if (duplicateEmailExists)
            {
                ModelState.AddModelError($"{nameof(Profile)}.{nameof(ProfileInputModel.Email)}", AppLocalizer.Get(
                    "Пользователь с таким адресом электронной почты уже существует.",
                    "Користувач із такою адресою електронної пошти вже існує.",
                    "A user with this email address already exists."));
            }
        }

        if (!ModelState.IsValid)
        {
            ReopenEditProfileModal = true;
            await LoadPageStateAsync(user, cancellationToken, preserveProfileInput: true);
            return Page();
        }

        var name = ParseFullName(Profile.FullName);

        member.FullName = Profile.FullName;
        member.PhoneNumber = Profile.PhoneNumber;
        member.Email = Profile.Email;
        member.UpdatedAtUtc = DateTime.UtcNow;

        user.FirstName = name.FirstName;
        user.LastName = name.LastName;
        user.DisplayName = name.DisplayName;
        user.PhoneNumber = Profile.PhoneNumber;
        user.Email = Profile.Email;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            ReopenEditProfileModal = true;
            IdentityErrorLocalizer.AddErrors(
                ModelState,
                updateResult,
                $"{nameof(Profile)}.{nameof(ProfileInputModel.Email)}",
                string.Empty);

            await LoadPageStateAsync(user, cancellationToken, preserveProfileInput: true);
            return Page();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        TempData["SuccessMessage"] = AppLocalizer.Get(
            "Основные сведения обновлены.",
            "Основні відомості оновлено.",
            "Profile details have been updated.");
        return RedirectToPage(new { chargePage = ChargePage, paymentPage = PaymentPage, chargeTypeId = ChargeTypeId });
    }

    public async Task<IActionResult> OnPostChangePasswordAsync(CancellationToken cancellationToken)
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

        if (!ModelState.IsValid)
        {
            ReopenChangePasswordModal = true;
            await LoadPageStateAsync(user, cancellationToken);
            return Page();
        }

        var result = await _userManager.ChangePasswordAsync(user, ChangePassword.CurrentPassword, ChangePassword.NewPassword);
        if (!result.Succeeded)
        {
            ReopenChangePasswordModal = true;
            IdentityErrorLocalizer.AddErrors(
                ModelState,
                result,
                string.Empty,
                $"{nameof(ChangePassword)}.{nameof(ChangePasswordInputModel.NewPassword)}",
                $"{nameof(ChangePassword)}.{nameof(ChangePasswordInputModel.CurrentPassword)}");

            await LoadPageStateAsync(user, cancellationToken);
            return Page();
        }

        TempData["SuccessMessage"] = AppLocalizer.Get(
            "Пароль успешно изменен.",
            "Пароль успішно змінено.",
            "The password has been changed successfully.");
        return RedirectToPage(new { chargePage = ChargePage, paymentPage = PaymentPage, chargeTypeId = ChargeTypeId });
    }

    private async Task LoadPageStateAsync(
        ApplicationUser user,
        CancellationToken cancellationToken,
        bool preserveProfileInput = false)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);
        ChargePage = ChargePage < 1 ? 1 : ChargePage;
        PaymentPage = PaymentPage < 1 ? 1 : PaymentPage;

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
                FullName = !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : user.Email ?? user.UserName ?? AppLocalizer.Get("Пользователь", "Користувач", "User"),
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                IsLinked = false
            };

            Plots = [];
            Charges = [];
            Payments = [];
            ElectricityMeters = [];
            ChargeTotalPages = 1;
            PaymentTotalPages = 1;
            IsElectricityFeatureAvailable = true;
            ElectricityFeatureWarningMessage = null;
            return;
        }

        if (!preserveProfileInput)
        {
            Profile = new ProfileInputModel
            {
                FullName = member.FullName,
                PhoneNumber = member.PhoneNumber,
                Email = member.Email
            };
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

        ChargeTypeOptions = await _dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.PlotId != null && plotIds.Contains(charge.PlotId.Value))
            .Select(charge => new
            {
                charge.ChargeTypeId,
                Name = charge.ChargeType != null ? charge.ChargeType.Name : null
            })
            .Distinct()
            .OrderBy(item => item.Name)
            .Select(item => new SelectListItem
            {
                Value = item.ChargeTypeId.ToString(),
                Text = string.IsNullOrWhiteSpace(item.Name) ? AppLocalizer.Get($"Тип #{item.ChargeTypeId}", $"Тип #{item.ChargeTypeId}", $"Type #{item.ChargeTypeId}") : item.Name
            })
            .ToListAsync(cancellationToken);

        if (ChargeTypeId.HasValue && !ChargeTypeOptions.Any(option => option.Value == ChargeTypeId.Value.ToString()))
        {
            ChargeTypeId = null;
        }

        var chargesQuery = _dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.PlotId != null && plotIds.Contains(charge.PlotId.Value));

        if (ChargeTypeId.HasValue)
        {
            chargesQuery = chargesQuery.Where(charge => charge.ChargeTypeId == ChargeTypeId.Value);
        }

        chargesQuery = chargesQuery
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
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static (string FirstName, string LastName, string DisplayName) ParseFullName(string fullName)
    {
        var trimmedFullName = fullName.Trim();
        var nameParts = trimmedFullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (nameParts.Length == 0)
        {
            return (
                AppLocalizer.Get("Член", "Член", "Member"),
                AppLocalizer.Get("Товарищества", "Товариства", "Association"),
                AppLocalizer.Get("Член товарищества", "Член товариства", "Association member"));
        }

        if (nameParts.Length == 1)
        {
            return (TrimToLength(nameParts[0], 100), AppLocalizer.Get("Товарищества", "Товариства", "Association"), trimmedFullName);
        }

        var firstName = TrimToLength(string.Join(' ', nameParts[..^1]), 100);
        var lastName = TrimToLength(nameParts[^1], 100);

        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = AppLocalizer.Get("Член", "Член", "Member");
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            lastName = AppLocalizer.Get("Товарищества", "Товариства", "Association");
        }

        return (firstName, lastName, trimmedFullName);
    }

    private static string TrimToLength(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
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

    public sealed class ProfileInputModel : IValidatableObject
    {
        public string FullName { get; set; } = string.Empty;

        public string? PhoneNumber { get; set; }

        public string? Email { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(FullName))
            {
                yield return new ValidationResult(
                    AppLocalizer.Get("Укажите ФИО.", "Вкажіть ПІБ.", "Enter the full name."),
                    [nameof(FullName)]);
            }
            else if (FullName.Trim().Length > 200)
            {
                yield return new ValidationResult(
                    AppLocalizer.Get("ФИО не должно превышать 200 символов.", "ПІБ не повинно перевищувати 200 символів.", "The full name must not exceed 200 characters."),
                    [nameof(FullName)]);
            }

            if (!string.IsNullOrWhiteSpace(PhoneNumber))
            {
                if (PhoneNumber.Trim().Length > 50)
                {
                    yield return new ValidationResult(
                        AppLocalizer.Get("Номер телефона не должен превышать 50 символов.", "Номер телефону не повинен перевищувати 50 символів.", "The phone number must not exceed 50 characters."),
                        [nameof(PhoneNumber)]);
                }
                else if (!new PhoneAttribute().IsValid(PhoneNumber))
                {
                    yield return new ValidationResult(
                        AppLocalizer.Get("Введите корректный номер телефона.", "Введіть коректний номер телефону.", "Enter a valid phone number."),
                        [nameof(PhoneNumber)]);
                }
            }

            if (!string.IsNullOrWhiteSpace(Email))
            {
                if (Email.Trim().Length > 256)
                {
                    yield return new ValidationResult(
                        AppLocalizer.Get("Электронная почта не должна превышать 256 символов.", "Електронна пошта не повинна перевищувати 256 символів.", "The email must not exceed 256 characters."),
                        [nameof(Email)]);
                }
                else if (!new EmailAddressAttribute().IsValid(Email))
                {
                    yield return new ValidationResult(
                        AppLocalizer.Get("Введите корректный адрес электронной почты.", "Введіть коректну адресу електронної пошти.", "Enter a valid email address."),
                        [nameof(Email)]);
                }
            }
        }
    }

    public sealed class ChangePasswordInputModel : IValidatableObject
    {
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        public string ConfirmNewPassword { get; set; } = string.Empty;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(CurrentPassword))
            {
                yield return new ValidationResult(
                    AppLocalizer.Get("Введите текущий пароль.", "Введіть поточний пароль.", "Enter the current password."),
                    [nameof(CurrentPassword)]);
            }

            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                yield return new ValidationResult(
                    AppLocalizer.Get("Введите новый пароль.", "Введіть новий пароль.", "Enter a new password."),
                    [nameof(NewPassword)]);
            }

            if (string.IsNullOrWhiteSpace(ConfirmNewPassword))
            {
                yield return new ValidationResult(
                    AppLocalizer.Get("Подтвердите новый пароль.", "Підтвердьте новий пароль.", "Confirm the new password."),
                    [nameof(ConfirmNewPassword)]);
            }
            else if (!string.IsNullOrWhiteSpace(NewPassword)
                && !string.Equals(NewPassword, ConfirmNewPassword, StringComparison.Ordinal))
            {
                yield return new ValidationResult(
                    AppLocalizer.Get("Пароли не совпадают.", "Паролі не збігаються.", "Passwords do not match."),
                    [nameof(ConfirmNewPassword)]);
            }
        }
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
                    .OrderBy(reading => reading.ReadingDate)
                    .ThenBy(reading => reading.Id)
                    .Select(reading => new
                    {
                        reading.MemberElectricityMeterId,
                        Item = new MemberElectricityReadingHistoryItemViewModel
                        {
                            Id = reading.Id,
                            ReadingDate = reading.ReadingDate,
                            CurrentReading = reading.CurrentReading,
                            Amount = reading.Amount,
                            IsInitialReading = reading.IsInitialReading
                        }
                    })
                    .ToListAsync(cancellationToken))
                    .GroupBy(item => item.MemberElectricityMeterId)
                    .ToDictionary(
                        group => group.Key,
                        group => BuildReadingHistory(group.Select(item => item.Item).ToList()));

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
            ElectricityFeatureWarningMessage = AppLocalizer.Get("Модуль электросчётчиков недоступен: необходимо применить обновление базы данных.", "Модуль електролічильників недоступний: необхідно застосувати оновлення бази даних.", "The electricity meter module is unavailable: a database update must be applied.");
        }
    }

    private static bool IsMissingTableException(SqlException exception)
    {
        return exception.Number == 208
            || exception.Message.Contains("MemberElectricityMeters", StringComparison.OrdinalIgnoreCase);
    }

    private static List<MemberElectricityReadingHistoryItemViewModel> BuildReadingHistory(List<MemberElectricityReadingHistoryItemViewModel> readings)
    {
        decimal? previousReading = null;
        foreach (var reading in readings)
        {
            reading.Consumption = !reading.IsInitialReading && previousReading.HasValue
                ? reading.CurrentReading - previousReading.Value
                : null;

            previousReading = reading.CurrentReading;
        }

        readings.Reverse();
        return readings;
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

        public string StatusText => IsCancelled
            ? AppLocalizer.Get("Отменено", "Скасовано", "Cancelled")
            : AppLocalizer.Get("Активно", "Активно", "Active");
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

        public string StatusText => IsCancelled
            ? AppLocalizer.Get("Отменено", "Скасовано", "Cancelled")
            : AppLocalizer.Get("Активно", "Активно", "Active");
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

        public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : !string.IsNullOrWhiteSpace(MeterNumber) ? MeterNumber : AppLocalizer.Get($"Счётчик #{Id}", $"Лічильник #{Id}", $"Meter #{Id}");
    }

    public sealed class MemberElectricityReadingHistoryItemViewModel
    {
        public long Id { get; init; }
        public DateOnly ReadingDate { get; init; }

        public decimal CurrentReading { get; init; }

        public decimal? Consumption { get; set; }

        public decimal? Amount { get; init; }

        public bool IsInitialReading { get; init; }
    }
}
