using Microsoft.AspNetCore.Mvc;
using Neftyanik.Portal.Application.Payments;

namespace Neftyanik.Portal.Web.ViewComponents;

public sealed class PaymentNotificationBellViewComponent : ViewComponent
{
    private readonly IPaymentNotificationService _paymentNotificationService;

    public PaymentNotificationBellViewComponent(IPaymentNotificationService paymentNotificationService)
    {
        _paymentNotificationService = paymentNotificationService;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!HttpContext.User.IsInRole(Neftyanik.Portal.Domain.Constants.RoleNames.Administrator)
            && !HttpContext.User.IsInRole(Neftyanik.Portal.Domain.Constants.RoleNames.Accountant))
        {
            return Content(string.Empty);
        }
        var pendingCount = await _paymentNotificationService.GetPendingCountAsync(HttpContext.RequestAborted);
        return View(new PaymentNotificationBellViewModel(pendingCount));
    }

    public sealed record PaymentNotificationBellViewModel(int PendingCount)
    {
        public bool HasPendingNotifications => PendingCount > 0;
    }
}
