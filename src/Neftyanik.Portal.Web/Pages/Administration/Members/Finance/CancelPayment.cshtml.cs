using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Payments;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Finance;

namespace Neftyanik.Portal.Web.Pages.Administration.Members.Finance;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CancelPaymentModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPaymentService _paymentService;

    public CancelPaymentModel(ApplicationDbContext dbContext, IPaymentService paymentService)
    {
        _dbContext = dbContext;
        _paymentService = paymentService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public PaymentCancelViewModel Payment { get; private set; } = new();

    public bool CanCancel => !Payment.IsCancelled;

    public async Task<IActionResult> OnGetAsync(int memberId, long paymentId, CancellationToken cancellationToken)
    {
        var payment = await LoadViewModelAsync(memberId, paymentId, cancellationToken);
        if (payment is null)
        {
            return NotFound();
        }

        Payment = payment;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int memberId, long paymentId, CancellationToken cancellationToken)
    {
        var payment = await LoadViewModelAsync(memberId, paymentId, cancellationToken);
        if (payment is null)
        {
            return NotFound();
        }

        Payment = payment;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _paymentService.CancelPaymentAsync(
            new CancelPaymentRequest(paymentId, Input.CancellationReason),
            cancellationToken);

        if (!result.Succeeded)
        {
            switch (result.Code)
            {
                case CancelPaymentResultCode.AlreadyCancelled:
                    TempData["ErrorMessage"] = "Платеж уже отменён.";
                    return RedirectToPage("/Administration/Members/Finance", new { id = memberId });
                case CancelPaymentResultCode.NotFound:
                    return NotFound();
                case CancelPaymentResultCode.InvalidCancellationReason:
                    ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.CancellationReason)}", "Укажите причину отмены платежа длиной до 500 символов.");
                    return Page();
                default:
                    ModelState.AddModelError(string.Empty, "Не удалось отменить платеж.");
                    return Page();
            }
        }

        TempData["SuccessMessage"] = "Платеж отменён.";
        return RedirectToPage("/Administration/Members/Finance", new { id = memberId });
    }

    private async Task<PaymentCancelViewModel?> LoadViewModelAsync(int memberId, long paymentId, CancellationToken cancellationToken)
    {
        var member = await _dbContext.Members
            .AsNoTracking()
            .Where(item => item.Id == memberId)
            .Select(item => new
            {
                item.Id,
                item.FullName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            return null;
        }

        var payment = await _dbContext.Payments
            .AsNoTracking()
            .Where(item => item.Id == paymentId
                && item.PlotId.HasValue
                && _dbContext.PlotOwnerships.Any(ownership => ownership.MemberId == memberId && ownership.PlotId == item.PlotId.Value))
            .Select(item => new PaymentCancelViewModel
            {
                PaymentId = item.Id,
                MemberId = member.Id,
                MemberFullName = member.FullName,
                PlotId = item.PlotId!.Value,
                PlotNumber = item.Plot != null ? item.Plot.Number : "—",
                PaymentDate = item.PaymentDate,
                Amount = item.Amount,
                PaymentMethodText = FinanceDisplayHelper.GetPaymentMethodText(item.PaymentMethod),
                ReferenceNumber = item.ReferenceNumber,
                Description = item.Description,
                IsCancelled = item.CancelledAtUtc != null,
                CancelledAtUtc = item.CancelledAtUtc,
                CancellationReason = item.CancellationReason,
                SourcePaymentNotificationId = item.PaymentNotification != null ? item.PaymentNotification.Id : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            return null;
        }

        var allocations = await _dbContext.PaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.PaymentId == paymentId)
            .OrderBy(allocation => allocation.ChargeId)
            .ThenBy(allocation => allocation.Id)
            .Select(allocation => new PaymentAllocationViewModel
            {
                ChargeId = allocation.ChargeId,
                ChargeDate = allocation.Charge != null ? allocation.Charge.ChargeDate : null,
                ChargePlotNumber = allocation.Charge != null && allocation.Charge.Plot != null ? allocation.Charge.Plot.Number : null,
                Amount = allocation.Amount
            })
            .ToListAsync(cancellationToken);

        return payment with { Allocations = allocations };
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Укажите причину отмены платежа.")]
        [StringLength(500, ErrorMessage = "Причина отмены платежа не должна превышать 500 символов.")]
        [Display(Name = "Причина отмены")]
        public string? CancellationReason { get; set; }
    }

    public sealed record PaymentCancelViewModel
    {
        public long PaymentId { get; init; }

        public int MemberId { get; init; }

        public string MemberFullName { get; init; } = string.Empty;

        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public DateOnly PaymentDate { get; init; }

        public decimal Amount { get; init; }

        public string PaymentMethodText { get; init; } = string.Empty;

        public string? ReferenceNumber { get; init; }

        public string? Description { get; init; }

        public bool IsCancelled { get; init; }

        public DateTime? CancelledAtUtc { get; init; }

        public string? CancellationReason { get; init; }

        public long? SourcePaymentNotificationId { get; init; }

        public IReadOnlyList<PaymentAllocationViewModel> Allocations { get; init; } = [];
    }

    public sealed class PaymentAllocationViewModel
    {
        public long ChargeId { get; init; }

        public DateOnly? ChargeDate { get; init; }

        public string? ChargePlotNumber { get; init; }

        public decimal Amount { get; init; }
    }
}
