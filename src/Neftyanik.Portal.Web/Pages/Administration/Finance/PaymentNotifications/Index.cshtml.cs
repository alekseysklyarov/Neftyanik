using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Logging;
using Neftyanik.Portal.Application.Payments;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.PaymentNotifications;

[Authorize(Roles = RoleNames.Administrator)]
public class IndexModel : PageModel
{
    private readonly IPaymentNotificationService _paymentNotificationService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(IPaymentNotificationService paymentNotificationService, UserManager<ApplicationUser> userManager, ILogger<IndexModel> logger)
    {
        _paymentNotificationService = paymentNotificationService;
        _userManager = userManager;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public PaymentNotificationStatus SelectedStatus { get; private set; } = PaymentNotificationStatus.Pending;

    public string SelectedStatusQuery => SelectedStatus.ToString();

    public IReadOnlyList<SelectListItem> StatusFilters { get; } =
    [
        new SelectListItem { Value = PaymentNotificationStatus.Pending.ToString(), Text = AppLocalizer.Get("Ожидают подтверждения", "Очікують підтвердження", "Pending") },
        new SelectListItem { Value = PaymentNotificationStatus.Confirmed.ToString(), Text = AppLocalizer.Get("Подтвержденные", "Підтверджені", "Confirmed") },
        new SelectListItem { Value = PaymentNotificationStatus.Rejected.ToString(), Text = AppLocalizer.Get("Отклоненные", "Відхилені", "Rejected") }
    ];

    public IReadOnlyList<PaymentNotificationListItem> Notifications { get; private set; } = [];

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            await LoadNotificationsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load administrator payment notifications.");
            ErrorMessage = AppLocalizer.Get(
                "Не удалось загрузить уведомления о платежах. Повторите попытку позже.",
                "Не вдалося завантажити повідомлення про платежі. Повторіть спробу пізніше.",
                "The payment notifications could not be loaded. Please try again later.");
            Notifications = [];
        }
    }

    public async Task<IActionResult> OnPostConfirmAsync(long notificationId, CancellationToken cancellationToken)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser is null)
        {
            return Challenge();
        }

        ResolveSelectedStatus();

        var result = await _paymentNotificationService.ConfirmAsync(
            new ConfirmPaymentNotificationRequest(
                notificationId,
                DateOnly.FromDateTime(DateTime.Today),
                null,
                currentUser.Id),
            cancellationToken);

        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = GetOperationMessage(result, isConfirmation: true);
        return RedirectToPage(new { status = SelectedStatusQuery });
    }

    public async Task<IActionResult> OnPostRejectAsync(long notificationId, string? administratorComment, CancellationToken cancellationToken)
    {
        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser is null)
        {
            return Challenge();
        }

        ResolveSelectedStatus();

        if (!string.IsNullOrWhiteSpace(administratorComment)
            && administratorComment.Trim().Length > PaymentNotification.AdministratorCommentMaxLength)
        {
            TempData["ErrorMessage"] = AppLocalizer.Get(
                $"Комментарий администратора не должен превышать {PaymentNotification.AdministratorCommentMaxLength} символов.",
                $"Коментар адміністратора не повинен перевищувати {PaymentNotification.AdministratorCommentMaxLength} символів.",
                $"The administrator comment must not exceed {PaymentNotification.AdministratorCommentMaxLength} characters.");
            return RedirectToPage(new { status = SelectedStatusQuery });
        }

        var result = await _paymentNotificationService.RejectAsync(
            new RejectPaymentNotificationRequest(notificationId, currentUser.Id, administratorComment),
            cancellationToken);

        TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = GetOperationMessage(result, isConfirmation: false);
        return RedirectToPage(new { status = SelectedStatusQuery });
    }

    private async Task LoadNotificationsAsync(CancellationToken cancellationToken)
    {
        ResolveSelectedStatus();

        Notifications = await _paymentNotificationService.GetForAdministrationAsync(
            new GetPaymentNotificationsForAdministrationRequest(SelectedStatus, 200),
            cancellationToken);
    }

    private void ResolveSelectedStatus()
    {
        SelectedStatus = Enum.TryParse<PaymentNotificationStatus>(Status, true, out var parsedStatus)
            ? parsedStatus
            : PaymentNotificationStatus.Pending;

        Status = SelectedStatus.ToString();
    }

    private static string GetOperationMessage(PaymentNotificationOperationResult result, bool isConfirmation)
    {
        if (result.Succeeded)
        {
            return isConfirmation
                ? AppLocalizer.Get(
                    "Уведомление о платеже подтверждено. Реальный платеж зарегистрирован.",
                    "Повідомлення про платіж підтверджено. Реальний платіж зареєстровано.",
                    "The payment notification has been confirmed. A real payment has been registered.")
                : AppLocalizer.Get(
                    "Уведомление о платеже отклонено.",
                    "Повідомлення про платіж відхилено.",
                    "The payment notification has been rejected.");
        }

        return result.Code switch
        {
            PaymentNotificationOperationResultCode.NotFound => AppLocalizer.Get(
                "Уведомление о платеже не найдено.",
                "Повідомлення про платіж не знайдено.",
                "The payment notification was not found."),
            PaymentNotificationOperationResultCode.AlreadyProcessed => AppLocalizer.Get(
                "Это уведомление уже было обработано другим пользователем.",
                "Це повідомлення вже було опрацьовано іншим користувачем.",
                "This notification has already been processed by another user."),
            PaymentNotificationOperationResultCode.PaymentCreationFailed => AppLocalizer.Get(
                "Не удалось зарегистрировать платеж по уведомлению. Уведомление осталось в статусе ожидания.",
                "Не вдалося зареєструвати платіж за повідомленням. Повідомлення залишилося в статусі очікування.",
                "The payment could not be registered from the notification. The notification remains pending."),
            PaymentNotificationOperationResultCode.InvalidRequest => isConfirmation
                ? AppLocalizer.Get(
                    "Не удалось подтвердить уведомление о платеже. Проверьте состояние записи и повторите попытку.",
                    "Не вдалося підтвердити повідомлення про платіж. Перевірте стан запису та повторіть спробу.",
                    "The payment notification could not be confirmed. Check the record state and try again.")
                : AppLocalizer.Get(
                    "Не удалось отклонить уведомление о платеже. Проверьте введенные данные и повторите попытку.",
                    "Не вдалося відхилити повідомлення про платіж. Перевірте введені дані та повторіть спробу.",
                    "The payment notification could not be rejected. Check the entered data and try again."),
            _ => AppLocalizer.Get(
                "Не удалось обработать уведомление о платеже. Повторите попытку позже.",
                "Не вдалося опрацювати повідомлення про платіж. Повторіть спробу пізніше.",
                "The payment notification could not be processed. Please try again later.")
        };
    }
}
