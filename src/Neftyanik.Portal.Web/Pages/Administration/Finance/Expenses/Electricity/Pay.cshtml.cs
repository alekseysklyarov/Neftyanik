using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using System.Security.Claims;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses.Electricity;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class PayModel(ApplicationDbContext db, IAssociationElectricityService service) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public decimal Remaining { get; private set; }
    public DateOnly ReadingDate { get; private set; }
    public async Task<IActionResult> OnGetAsync(long readingId, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(readingId, cancellationToken)) return NotFound();
        Input.Amount = Remaining;
        Input.PaymentDate = DateOnly.FromDateTime(DateTime.Today);
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(long readingId, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(readingId, cancellationToken)) return NotFound();
        if (!ModelState.IsValid) return Page();
        var result = await service.CreateExpenseAsync(new(readingId, User.FindFirstValue(ClaimTypes.NameIdentifier),
            Input.PaymentDate, Input.Amount, Input.PaymentMethod, Input.DocumentNumber), cancellationToken);
        if (!result.Succeeded) { ModelState.AddModelError(string.Empty, result.ErrorMessage!); return Page(); }
        TempData["SuccessMessage"] = "Оплата поставщику зарегистрирована.";
        return RedirectToPage("Index");
    }
    private async Task<bool> LoadAsync(long id, CancellationToken ct)
    {
        var reading = await db.AssociationElectricityReadings.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && !r.IsInitialReading, ct);
        if (reading is null) return false;
        var payments = await db.Expenses.AsNoTracking().Where(e => e.AssociationElectricityReadingId == id && !e.IsCancelled).Select(e => e.Amount).ToListAsync(ct);
        ReadingDate = reading.ReadingDate;
        Remaining = Math.Max((reading.TotalSupplierAmount ?? 0m) - payments.Sum(), 0m);
        return true;
    }
    public sealed class InputModel
    {
        [Required, DataType(DataType.Date), Display(Name = "Дата фактической оплаты")] public DateOnly? PaymentDate { get; set; }
        [Required, Range(typeof(decimal), "0.01", "999999999999", ParseLimitsInInvariantCulture = true), Display(Name = "Сумма оплаты, грн")] public decimal? Amount { get; set; }
        [Range(1, 2), Display(Name = "Оплачено из")] public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.BankTransfer;
        [StringLength(100), Display(Name = "Номер платёжного документа")] public string? DocumentNumber { get; set; }
    }
}
