using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters.Readings;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class DeleteModel(ApplicationDbContext db, IFinancialAuditService audit) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public ReadingViewModel Reading { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, long readingId, CancellationToken ct)
    {
        var reading = await db.MemberElectricityReadings.AsNoTracking()
            .Where(r => r.Id == readingId && r.MemberElectricityMeterId == id)
            .Select(r => new ReadingViewModel
            {
                Id = r.Id, Date = r.ReadingDate, Day = r.CurrentReading, Night = r.CurrentNightReading,
                PhysicalMeterReadingsJson = r.PhysicalMeterReadingsJson, IsInitialReading = r.IsInitialReading,
                ChargeAmount = r.Charge != null ? r.Charge.Amount : null,
                MemberId = r.MemberElectricityMeter!.MemberId,
                MemberName = r.MemberElectricityMeter.Member!.FullName,
                MeterName = r.MemberElectricityMeter.Name ?? r.MemberElectricityMeter.MeterNumber ?? "—"
            }).SingleOrDefaultAsync(ct);
        if (reading is null) return NotFound();
        if (await db.MemberElectricityReadings.AnyAsync(r => r.MemberElectricityMeterId == id
            && (r.ReadingDate > reading.Date || (r.ReadingDate == reading.Date && r.Id > reading.Id)), ct))
            return BadRequest(LatestOnlyMessage());
        Reading = reading;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, long readingId, CancellationToken ct)
    {
        var loaded = await OnGetAsync(id, readingId, ct);
        if (loaded is not PageResult) return loaded;
        if (!ModelState.IsValid) return Page();
        var result = await new MemberReadingDeletionService(db, audit).DeleteLatestAsync(id, readingId,
            Input.Reason, User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", ct);
        switch (result.Code)
        {
            case MemberReadingDeletionResultCode.Success:
                TempData["SuccessMessage"] = AppLocalizer.Get(
                    "Последнее показание удалено. Связанное начисление отменено, внесённые оплаты сохранены.",
                    "Останній показник видалено. Пов'язане нарахування скасовано, внесені оплати збережено.",
                    "The latest reading was deleted. Its charge was cancelled and recorded payments were preserved.");
                return RedirectToPage("/Administration/Members/Finance", new { id = Reading.MemberId });
            case MemberReadingDeletionResultCode.NotFound:
                return NotFound();
            case MemberReadingDeletionResultCode.NotLatest:
                return BadRequest(LatestOnlyMessage());
            case MemberReadingDeletionResultCode.InvalidActor:
                return Forbid();
            default:
                ModelState.AddModelError(string.Empty, AppLocalizer.Get(
                    "Не удалось удалить показание. Проверьте причину удаления и обновите страницу.",
                    "Не вдалося видалити показник. Перевірте причину видалення та оновіть сторінку.",
                    "Could not delete the reading. Check the deletion reason and refresh the page."));
                return Page();
        }
    }

    private static string LatestOnlyMessage() => AppLocalizer.Get(
        "Можно удалить только последнее показание счётчика. Обновите страницу участника.",
        "Можна видалити лише останній показник лічильника. Оновіть сторінку учасника.",
        "Only the latest meter reading can be deleted. Refresh the member page.");

    public sealed class InputModel : IValidatableObject
    {
        public string? Reason { get; set; }
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Reason) || Reason.Trim().Length > 500)
                yield return new ValidationResult(AppLocalizer.Get(
                    "Укажите причину удаления (до 500 символов).", "Вкажіть причину видалення (до 500 символів).",
                    "Enter a deletion reason (up to 500 characters)."), [nameof(Reason)]);
        }
    }

    public sealed class ReadingViewModel
    {
        public long Id { get; init; }
        public int MemberId { get; init; }
        public string MemberName { get; init; } = "";
        public string MeterName { get; init; } = "";
        public DateOnly Date { get; init; }
        public decimal Day { get; init; }
        public decimal? Night { get; init; }
        public string? PhysicalMeterReadingsJson { get; init; }
        public decimal? ChargeAmount { get; init; }
        public bool IsInitialReading { get; init; }
    }
}
