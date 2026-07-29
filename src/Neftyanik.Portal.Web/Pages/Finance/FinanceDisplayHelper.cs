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
            PaymentMethod.Card => AppLocalizer.Get("Карта", "Картка", "Card"),
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
}
