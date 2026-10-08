using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class UnassignedChargesModel(ApplicationDbContext db, IFinancialAuditService audit) : PageModel
{
    public List<Charge> Charges { get; private set; } = [];
    public List<SelectListItem> Members { get; private set; } = [];
    public int Total { get; private set; }
    public async Task OnGetAsync(CancellationToken ct)
    {
        Total = await db.Charges.CountAsync(c => c.MemberId == null, ct);
        Charges = await db.Charges.AsNoTracking().Include(c => c.Plot).Include(c => c.ChargeType)
            .Where(c => c.MemberId == null).OrderBy(c => c.ChargeDate).ThenBy(c => c.Id).Take(100).ToListAsync(ct);
        Members = await db.Members.AsNoTracking().OrderBy(m => m.FullName)
            .Select(m => new SelectListItem(m.FullName + (m.IsActive ? "" : " (архив)"), m.Id.ToString())).ToListAsync(ct);
    }
    public async Task<IActionResult> OnPostAsync(long chargeId, int? memberId, string? reason, CancellationToken ct)
    {
        if (!memberId.HasValue || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500
            || !await db.Members.AnyAsync(m => m.Id == memberId, ct))
        {
            ModelState.AddModelError(string.Empty, "Выберите плательщика и укажите основание (до 500 символов).");
            await OnGetAsync(ct);
            return Page();
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvancePaymentAllocator.LockAsync(db, ct);
        var charge = await db.Charges.SingleOrDefaultAsync(c => c.Id == chargeId && c.MemberId == null, ct);
        if (charge is null) return NotFound();
        charge.MemberId = memberId;
        audit.Add(FinancialAuditLogActions.Updated, nameof(Charge), charge.Id.ToString(),
            "Уточнён плательщик перенесённого начисления: " + reason.Trim(),
            new { MemberId = (int?)null }, new { MemberId = memberId, Reason = reason.Trim() });
        await db.SaveChangesAsync(ct);
        await AdvancePaymentAllocator.ApplyAsync(db, audit, [charge.Id], ct);
        await transaction.CommitAsync(ct);
        TempData["SuccessMessage"] = "Плательщик сохранён. Начисление включено в его финансовую историю.";
        return RedirectToPage();
    }
}
