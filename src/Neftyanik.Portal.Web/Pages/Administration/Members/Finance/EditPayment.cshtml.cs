using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;

namespace Neftyanik.Portal.Web.Pages.Administration.Members.Finance;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class EditPaymentModel(ApplicationDbContext db, PaymentCorrectionService corrections) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public Payment Payment { get; private set; } = null!;
    public bool CanCorrect { get; private set; }
    public async Task<IActionResult> OnGetAsync(int memberId, long paymentId, CancellationToken ct)
    {
        if (!await LoadAsync(memberId, paymentId, ct)) return NotFound();
        Input = new() { Amount = Payment.Amount, PaymentMethod = Payment.PaymentMethod,
            ReferenceNumber = Payment.ReferenceNumber, Description = Payment.Description, Version = PaymentCorrectionService.Version(Payment) };
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(int memberId, long paymentId, CancellationToken ct)
    {
        if (!await LoadAsync(memberId, paymentId, ct)) return NotFound();
        if (!CanCorrect) { ModelState.AddModelError(string.Empty, "Можно исправить только последний активный платёж участника."); return Page(); }
        if (!ModelState.IsValid) return Page();
        var error = await corrections.CorrectAsync(memberId, paymentId, Input.Amount!.Value, Input.PaymentMethod!.Value,
            Input.ReferenceNumber, Input.Description, Input.Reason, Input.Version, ct);
        if (error is not null) { ModelState.AddModelError(string.Empty, error); return Page(); }
        TempData["SuccessMessage"] = "Платёж исправлен. Распределение оплаты и баланс пересчитаны, прежние значения сохранены в журнале.";
        return RedirectToPage("/Administration/Members/Finance", new { id = memberId });
    }
    private async Task<bool> LoadAsync(int memberId, long paymentId, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().Include(p => p.Member).Include(p => p.Plot)
            .Include(p => p.PaymentAllocations).ThenInclude(a => a.Charge)
            .SingleOrDefaultAsync(p => p.Id == paymentId && p.MemberId == memberId, ct);
        if (payment is null) return false;
        Payment = payment;
        CanCorrect = payment.CancelledAtUtc is null && payment.Id == await PaymentCorrectionService.LatestActivePaymentIdAsync(db, memberId, ct);
        return true;
    }
    public sealed class InputModel
    {
        [Required, Display(Name = "Сумма, грн")] public decimal? Amount { get; set; }
        [Required, Display(Name = "Способ оплаты")] public PaymentMethod? PaymentMethod { get; set; }
        [StringLength(150), Display(Name = "Номер документа / квитанции")] public string? ReferenceNumber { get; set; }
        [StringLength(1000), Display(Name = "Описание")] public string? Description { get; set; }
        [Required, StringLength(500), Display(Name = "Причина исправления")] public string Reason { get; set; } = "";
        [Required, StringLength(64)] public string Version { get; set; } = "";
    }
}
