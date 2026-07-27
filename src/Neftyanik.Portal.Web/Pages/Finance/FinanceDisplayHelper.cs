using Neftyanik.Portal.Domain.Enums;

namespace Neftyanik.Portal.Web.Pages.Finance;

internal static class FinanceDisplayHelper
{
    public static string GetPaymentMethodText(PaymentMethod method)
    {
        return method switch
        {
            PaymentMethod.Cash => "Наличные",
            PaymentMethod.BankTransfer => "Банковский перевод",
            PaymentMethod.Card => "Карта",
            _ => "Другое"
        };
    }

    public static string GetBalanceStatusText(decimal balance)
    {
        return balance switch
        {
            > 0m => "Задолженность",
            < 0m => "Переплата",
            _ => "Задолженности нет"
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
