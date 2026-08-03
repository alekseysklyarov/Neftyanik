using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Finance;

internal static class FinanceDisplayHelper
{
    public static string GetPaymentMethodText(PaymentMethod method)
    {
        return method switch
        {
            PaymentMethod.Cash => AppLocalizer.Get("Наличные", "Готівка", "Cash"),
            PaymentMethod.BankTransfer => AppLocalizer.Get("Банковский перевод", "Банківський переказ", "Bank transfer"),
            PaymentMethod.Card => AppLocalizer.Get("Перевод на карту", "Переказ на картку", "Card transfer"),
            _ => AppLocalizer.Get("Другое", "Інше", "Other")
        };
    }

    public static string GetBalanceStatusText(decimal balance)
    {
        return balance switch
        {
            > 0m => AppLocalizer.Get("Задолженность", "Заборгованість", "Debt"),
            < 0m => AppLocalizer.Get("Переплата", "Переплата", "Overpayment"),
            _ => AppLocalizer.Get("Задолженности нет", "Заборгованості немає", "No debt")
        };
    }

    public static string GetBalanceStatusClass(decimal balance)
    {
        return balance switch
        {
            > 0m => "text-danger",
            < 0m => "text-primary",
            _ => "text-success"
        };
    }

    public static string GetPaymentNotificationStatusText(PaymentNotificationStatus status)
    {
        return status switch
        {
            PaymentNotificationStatus.Pending => AppLocalizer.Get("Ожидает подтверждения", "Очікує підтвердження", "Awaiting confirmation"),
            PaymentNotificationStatus.Confirmed => AppLocalizer.Get("Подтверждено", "Підтверджено", "Confirmed"),
            PaymentNotificationStatus.Rejected => AppLocalizer.Get("Отклонено", "Відхилено", "Rejected"),
            _ => status.ToString()
        };
    }

    public static string GetPaymentNotificationStatusBadgeClass(PaymentNotificationStatus status)
    {
        return status switch
        {
            PaymentNotificationStatus.Pending => "bg-warning text-dark",
            PaymentNotificationStatus.Confirmed => "bg-success text-white",
            PaymentNotificationStatus.Rejected => "bg-danger text-white",
            _ => "bg-secondary text-white"
        };
    }
}
