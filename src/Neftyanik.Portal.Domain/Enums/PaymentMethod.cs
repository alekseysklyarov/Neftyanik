namespace Neftyanik.Portal.Domain.Enums;

public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    Card = 3,
    Other = 4,
    // Account credit transferred at onboarding; not a cash or bank receipt.
    OpeningBalance = 5
}
