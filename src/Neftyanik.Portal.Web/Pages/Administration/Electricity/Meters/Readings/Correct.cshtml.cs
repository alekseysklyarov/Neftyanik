using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters.Readings;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CorrectModel(ApplicationDbContext db, IFinancialAuditService audit) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public bool HasNight { get; private set; }
    public DateOnly ReadingDate { get; private set; }
    public int MemberId { get; private set; }
    public async Task<IActionResult> OnGetAsync(int id, long readingId, CancellationToken ct)
    {
        var reading = await db.MemberElectricityReadings.AsNoTracking().Include(r => r.MemberElectricityMeter)
            .SingleOrDefaultAsync(r => r.Id == readingId && r.MemberElectricityMeterId == id, ct);
        if (reading is null) return NotFound();
        if (await db.MemberElectricityReadings.AnyAsync(r => r.MemberElectricityMeterId == id
            && (r.ReadingDate > reading.ReadingDate || (r.ReadingDate == reading.ReadingDate && r.Id > reading.Id)), ct))
            return BadRequest("Можно исправить только последнее показание счётчика. Обновите страницу участника.");
        MemberId = reading.MemberElectricityMeter!.MemberId;
        ReadingDate = reading.ReadingDate; HasNight = reading.CurrentNightReading.HasValue;
        Input = new InputModel { Day = reading.CurrentReading, Night = reading.CurrentNightReading };
        return Page();
    }
    public async Task<IActionResult> OnPostAsync(int id, long readingId, CancellationToken ct)
    {
        var posted = Input;
        var result = await OnGetAsync(id, readingId, ct);
        Input = posted;
        if (result is not PageResult) return result;
        if (!ModelState.IsValid) return Page();
        var correction = await new MemberReadingCorrectionService(db, audit).CorrectAsync(id, readingId, Input.Day!.Value,
            Input.Night, Input.Reason, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", ct);
        if (!correction.Succeeded) { ModelState.AddModelError(string.Empty, correction.ErrorMessage!); return Page(); }
        TempData["SuccessMessage"] = "Показания исправлены. Начисления и распределение оплат пересчитаны; история сохранена.";
        return RedirectToPage("/Administration/Members/Finance", new { id = MemberId });
    }
    public sealed class InputModel
    {
        [Required, Range(typeof(decimal), "0", "999999999999"), Display(Name = "Показание Т1")] public decimal? Day { get; set; }
        [Range(typeof(decimal), "0", "999999999999"), Display(Name = "Показание Т2")] public decimal? Night { get; set; }
        [Required, StringLength(500), Display(Name = "Причина исправления")] public string Reason { get; set; } = "";
    }
}
