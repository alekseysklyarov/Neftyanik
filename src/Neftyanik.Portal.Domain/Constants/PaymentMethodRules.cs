using Neftyanik.Portal.Domain.Enums;

namespace Neftyanik.Portal.Domain.Constants;

public static class PaymentMethodRules
{
    public static readonly IReadOnlyList<PaymentMethod> AllowedMethods =
    [
        PaymentMethod.Cash,
        PaymentMethod.Card
    ];

    public static bool IsAllowed(PaymentMethod method)
    {
        return method is PaymentMethod.Cash or PaymentMethod.Card;
    }
}
