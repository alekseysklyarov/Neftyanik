using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Administration.Finance;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Settings;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CashInitializationModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public CashInitializationModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public CashInitializationViewModel? CashInitialization { get; private set; }

    public bool IsReadOnly => CashInitialization is not null;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadCashInitializationAsync(cancellationToken);

        if (!IsReadOnly)
        {
            Input.AcceptedAt = DateOnly.FromDateTime(DateTime.Today);
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadCashInitializationAsync(cancellationToken);
        if (IsReadOnly)
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

    private async Task LoadCashInitializationAsync(CancellationToken cancellationToken)
    {
        var setting = await _dbContext.SystemSettings
            .AsNoTracking()
            .Where(item => item.Key == CashInitializationSettingSerializer.SettingKey)
            .Select(item => new ExistingSettingViewModel
            {
                Value = item.Value,
                UpdatedAt = item.UpdatedAt,
                UpdatedByUserId = item.UpdatedByUserId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (setting is null)
        {
            CashInitialization = null;
            return;
        }

        var data = CashInitializationSettingSerializer.Deserialize(setting.Value);
        if (data is null)
        {
            CashInitialization = null;
            ModelState.AddModelError(string.Empty, "Не удалось прочитать данные инициализации кассы.");
            return;
        }

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

    public sealed class CashInitializationViewModel
    {
        public decimal Amount { get; init; }

        public decimal AdvancePaymentsAmount { get; init; }

        public DateOnly AcceptedAt { get; init; }

        public string AcceptedFrom { get; init; } = string.Empty;

        public string AcceptedBy { get; init; } = string.Empty;

        public DateTimeOffset SavedAt { get; init; }
    }

    private sealed class ExistingSettingViewModel
    {
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
