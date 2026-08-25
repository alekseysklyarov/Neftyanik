using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Administration.Finance;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Settings;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CashInitializationModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFinancialAuditService _financialAuditService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CashInitializationModel(
        ApplicationDbContext dbContext,
        IFinancialAuditService financialAuditService,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _financialAuditService = financialAuditService;
        _userManager = userManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public AdjustmentInputModel Adjustment { get; set; } = new();

    public CashInitializationViewModel? CashInitialization { get; private set; }

    public IReadOnlyList<CashInitializationAuditEntryViewModel> AdjustmentHistory { get; private set; } = [];

    public bool HasExistingSetting { get; private set; }

    public bool HasUnreadableCashInitialization { get; private set; }

    public bool IsReadOnly => CashInitialization is not null;

    public bool IsAdministrator => User.IsInRole(RoleNames.Administrator);

    public bool CanCreate => IsAdministrator && !HasExistingSetting && !HasUnreadableCashInitialization;

    public bool CanAdjust => IsAdministrator && CashInitialization is not null;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadCashInitializationAsync(cancellationToken);

        if (!HasExistingSetting)
        {
            Input.AcceptedAt = DateOnly.FromDateTime(DateTime.Today);
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!IsAdministrator)
        {
            return Forbid();
        }

        await LoadCashInitializationAsync(cancellationToken);
        if (HasExistingSetting)
        {
            ModelState.AddModelError(string.Empty, "Инициализация кассы уже выполнена. Доступен только просмотр.");
            return Page();
        }

        if (Input.Amount.HasValue && Input.Amount.Value <= 0m)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.Amount)}", "Сумма должна быть больше нуля.");
        }

        if (Input.AdvancePaymentsAmount.HasValue && Input.AdvancePaymentsAmount.Value < 0m)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.AdvancePaymentsAmount)}", "Сумма дебиторской задолженности не может быть отрицательной.");
        }

        if (!Input.IsConfirmed)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.IsConfirmed)}", "Подтвердите одноразовое сохранение суммы кассы.");
        }

        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var cashInitialization = new CashInitializationSettingSerializer.CashInitializationSettingData(
            decimal.Round(Input.Amount!.Value, 2, MidpointRounding.AwayFromZero),
            Input.AcceptedAt!.Value,
            Input.AcceptedFrom!.Trim(),
            decimal.Round(Input.AdvancePaymentsAmount ?? 0m, 2, MidpointRounding.AwayFromZero));

        _dbContext.SystemSettings.Add(new SystemSetting
        {
            Key = CashInitializationSettingSerializer.SettingKey,
            Value = CashInitializationSettingSerializer.Serialize(cashInitialization),
            Description = CashInitializationSettingSerializer.SettingDescription,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedByUserId = currentUser.Id
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await LoadCashInitializationAsync(cancellationToken);
            ModelState.AddModelError(string.Empty, "Инициализация кассы уже выполнена. Повторное сохранение недоступно.");
            return Page();
        }

        TempData["SuccessMessage"] = "Инициализация кассы сохранена.";
        return RedirectToPage("/Administration/Finance/Settings/CashInitialization");
    }

    public async Task<IActionResult> OnPostAdjustAsync(CancellationToken cancellationToken)
    {
        if (!IsAdministrator)
        {
            return Forbid();
        }

        Adjustment.AdjustmentReason = Adjustment.AdjustmentReason?.Trim();

        if (!ModelState.IsValid)
        {
            await LoadCashInitializationAsync(cancellationToken);
            return Page();
        }

        if (Adjustment.Amount.HasValue && Adjustment.Amount.Value <= 0m)
        {
            ModelState.AddModelError($"{nameof(Adjustment)}.{nameof(AdjustmentInputModel.Amount)}", "Сумма должна быть больше нуля.");
        }

        if (string.IsNullOrWhiteSpace(Adjustment.AdjustmentReason))
        {
            ModelState.AddModelError($"{nameof(Adjustment)}.{nameof(AdjustmentInputModel.AdjustmentReason)}", "Укажите причину корректировки.");
        }
        else if (Adjustment.AdjustmentReason.Length > 500)
        {
            ModelState.AddModelError($"{nameof(Adjustment)}.{nameof(AdjustmentInputModel.AdjustmentReason)}", "Причина корректировки не должна превышать 500 символов.");
        }

        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser is null)
        {
            return Challenge();
        }

        var setting = await _dbContext.SystemSettings
            .FirstOrDefaultAsync(item => item.Key == CashInitializationSettingSerializer.SettingKey, cancellationToken);

        if (setting is null)
        {
            await LoadCashInitializationAsync(cancellationToken);
            ModelState.AddModelError(string.Empty, "Инициализация кассы ещё не выполнена.");
            return Page();
        }

        var currentData = CashInitializationSettingSerializer.Deserialize(setting.Value);
        if (currentData is null)
        {
            await LoadCashInitializationAsync(cancellationToken);
            ModelState.AddModelError(string.Empty, "Не удалось прочитать данные инициализации кассы. Изменение не сохранено.");
            return Page();
        }

        if (!ModelState.IsValid)
        {
            await LoadCashInitializationAsync(cancellationToken);
            return Page();
        }

        var newAmount = decimal.Round(Adjustment.Amount!.Value, 2, MidpointRounding.AwayFromZero);
        if (newAmount == currentData.Amount)
        {
            TempData["InfoMessage"] = "Сумма не изменилась.";
            return RedirectToPage("/Administration/Finance/Settings/CashInitialization");
        }

        var updatedData = currentData with { Amount = newAmount };
        setting.Value = CashInitializationSettingSerializer.Serialize(updatedData);
        setting.UpdatedAt = DateTimeOffset.UtcNow;
        setting.UpdatedByUserId = currentUser.Id;

        _financialAuditService.Add(
            FinancialAuditLogActions.Updated,
            nameof(SystemSetting),
            setting.Id.ToString(),
            $"Скорректирована инициализация кассы. Причина: {Adjustment.AdjustmentReason}",
            oldValues: new
            {
                Amount = currentData.Amount
            },
            newValues: new
            {
                Amount = newAmount
            });

        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = "Начальная сумма кассы скорректирована.";
        return RedirectToPage("/Administration/Finance/Settings/CashInitialization");
    }

    private async Task LoadCashInitializationAsync(CancellationToken cancellationToken)
    {
        var setting = await _dbContext.SystemSettings
            .AsNoTracking()
            .Where(item => item.Key == CashInitializationSettingSerializer.SettingKey)
            .Select(item => new ExistingSettingViewModel
            {
                Id = item.Id,
                Value = item.Value,
                UpdatedAt = item.UpdatedAt,
                UpdatedByUserId = item.UpdatedByUserId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (setting is null)
        {
            HasExistingSetting = false;
            HasUnreadableCashInitialization = false;
            CashInitialization = null;
            AdjustmentHistory = [];
            return;
        }

        HasExistingSetting = true;

        var data = CashInitializationSettingSerializer.Deserialize(setting.Value);
        if (data is null)
        {
            HasUnreadableCashInitialization = true;
            CashInitialization = null;
            AdjustmentHistory = await LoadAdjustmentHistoryAsync(setting.Id.ToString(), cancellationToken);
            ModelState.AddModelError(string.Empty, "Не удалось прочитать данные инициализации кассы.");
            return;
        }

        HasUnreadableCashInitialization = false;

        var acceptedBy = "—";
        if (!string.IsNullOrWhiteSpace(setting.UpdatedByUserId))
        {
            var user = await _dbContext.Users
                .AsNoTracking()
                .Where(item => item.Id == setting.UpdatedByUserId)
                .Select(item => new UserDisplayViewModel
                {
                    DisplayName = item.DisplayName,
                    FirstName = item.FirstName,
                    LastName = item.LastName,
                    UserName = item.UserName
                })
                .FirstOrDefaultAsync(cancellationToken);

            acceptedBy = FormatUserDisplayName(user);
        }

        CashInitialization = new CashInitializationViewModel
        {
            Amount = data.Amount,
            AdvancePaymentsAmount = data.AdvancePaymentsAmount,
            AcceptedAt = data.AcceptedAt,
            AcceptedFrom = data.AcceptedFrom,
            AcceptedBy = acceptedBy,
            SavedAt = setting.UpdatedAt
        };

        AdjustmentHistory = await LoadAdjustmentHistoryAsync(setting.Id.ToString(), cancellationToken);
    }

    private async Task<IReadOnlyList<CashInitializationAuditEntryViewModel>> LoadAdjustmentHistoryAsync(string settingId, CancellationToken cancellationToken)
    {
        var entries = await _dbContext.FinancialAuditLogs
            .AsNoTracking()
            .Where(item => item.EntityType == nameof(SystemSetting)
                && item.EntityId == settingId
                && item.Action == FinancialAuditLogActions.Updated)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .ToListAsync(cancellationToken);

        return entries
            .Select(item => new CashInitializationAuditEntryViewModel
            {
                AuditLogId = item.Id,
                ChangedAtUtc = item.CreatedAtUtc,
                UserId = item.UserId,
                UserName = item.UserName,
                Description = item.Description,
                OldAmount = TryReadAmount(item.OldValuesJson),
                NewAmount = TryReadAmount(item.NewValuesJson)
            })
            .ToList();
    }

    private static decimal? TryReadAmount(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty("Amount", out var amountElement)
                || amountElement.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            return amountElement.GetDecimal();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FormatUserDisplayName(UserDisplayViewModel? user)
    {
        if (user is null)
        {
            return "—";
        }

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            return user.DisplayName.Trim();
        }

        var fullName = string.Join(' ', new[] { user.LastName, user.FirstName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName;
        }

        return string.IsNullOrWhiteSpace(user.UserName)
            ? "—"
            : user.UserName;
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Укажите сумму в кассе.")]
        [Display(Name = "Сумма в кассе")]
        public decimal? Amount { get; set; }

        [Display(Name = "Дебиторская задолженность")]
        public decimal? AdvancePaymentsAmount { get; set; }

        [Required(ErrorMessage = "Укажите дату принятия суммы.")]
        [Display(Name = "Дата принятия суммы")]
        public DateOnly? AcceptedAt { get; set; }

        [Required(ErrorMessage = "Укажите, от кого принята сумма.")]
        [StringLength(200, ErrorMessage = "Поле не должно превышать 200 символов.")]
        [Display(Name = "От кого принята сумма")]
        public string AcceptedFrom { get; set; } = string.Empty;

        [Display(Name = "Подтверждаю, что сумма вводится один раз и далее будет доступна только для просмотра")]
        public bool IsConfirmed { get; set; }
    }

    public sealed class AdjustmentInputModel
    {
        [Required(ErrorMessage = "Укажите сумму в кассе.")]
        [Display(Name = "Новая сумма")]
        public decimal? Amount { get; set; }

        [Required(ErrorMessage = "Укажите причину корректировки.")]
        [StringLength(500, ErrorMessage = "Причина корректировки не должна превышать 500 символов.")]
        [Display(Name = "Причина корректировки")]
        public string? AdjustmentReason { get; set; }
    }

    public sealed class CashInitializationViewModel
    {
        public decimal Amount { get; init; }

        public decimal AdvancePaymentsAmount { get; init; }

        public DateOnly AcceptedAt { get; init; }

        public string AcceptedFrom { get; init; } = string.Empty;

        public string AcceptedBy { get; init; } = string.Empty;

        public DateTimeOffset SavedAt { get; init; }
    }

    public sealed class CashInitializationAuditEntryViewModel
    {
        public long AuditLogId { get; init; }

        public DateTime ChangedAtUtc { get; init; }

        public string? UserId { get; init; }

        public string? UserName { get; init; }

        public string? Description { get; init; }

        public decimal? OldAmount { get; init; }

        public decimal? NewAmount { get; init; }

        public string UserDisplayName => !string.IsNullOrWhiteSpace(UserName)
            ? UserName
            : !string.IsNullOrWhiteSpace(UserId)
                ? UserId
                : "—";
    }

    private sealed class ExistingSettingViewModel
    {
        public int Id { get; init; }

        public string Value { get; init; } = string.Empty;

        public DateTimeOffset UpdatedAt { get; init; }

        public string? UpdatedByUserId { get; init; }
    }

    private sealed class UserDisplayViewModel
    {
        public string? DisplayName { get; init; }

        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        public string? UserName { get; init; }
    }
}
