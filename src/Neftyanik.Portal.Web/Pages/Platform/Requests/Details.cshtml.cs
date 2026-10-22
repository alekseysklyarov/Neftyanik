using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;
using Neftyanik.Portal.Web.Sales;

namespace Neftyanik.Portal.Web.Pages.Platform.Requests;

public class DetailsModel(ApplicationDbContext database) : PageModel
{
    [BindProperty] public PilotInput Input { get; set; } = new();
    [TempData] public bool Saved { get; set; }
    public DemoRequest Inquiry { get; private set; } = null!;
    public IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Stages =>
        PilotStages.Values.Select(x => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(PilotStages.Label(x), x));

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var inquiry = await database.DemoRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (inquiry is null) return NotFound();
        Inquiry = inquiry;
        Input = new PilotInput { Stage = inquiry.PilotStage, Notes = inquiry.PilotNotes, NextAction = inquiry.NextAction,
            NextActionDate = inquiry.NextActionDate, StartDate = inquiry.PilotStartDate,
            EndDate = inquiry.PilotEndDate, Revision = inquiry.PilotRevision };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var inquiry = await database.DemoRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (inquiry is null) return NotFound();
        Inquiry = inquiry;
        if (!PilotStages.Values.Contains(Input.Stage))
        {
            if (ModelState.TryGetValue("Input.Stage", out var stageEntry)) stageEntry.Errors.Clear();
            ModelState.AddModelError("Input.Stage", L("Выберите этап из списка.", "Оберіть етап зі списку.", "Choose a stage from the list."));
        }
        foreach (var key in new[] { "Input.Notes", "Input.NextAction", "Input.NextActionDate", "Input.StartDate", "Input.EndDate", "Input.Revision" })
            if (ModelState.TryGetValue(key, out var entry) && entry.Errors.Count > 0)
            {
                entry.Errors.Clear();
                entry.Errors.Add(L("Проверьте значение и указанное ограничение поля.", "Перевірте значення та зазначене обмеження поля.", "Check the value and the field’s stated limit."));
            }
        if (Input.Stage is "pilot" or "completed" && (Input.StartDate is null || Input.EndDate is null))
            ModelState.AddModelError("Input.StartDate", L("Для пилота укажите обе даты.", "Для пілоту зазначте обидві дати.", "Enter both dates for a pilot."));
        if (Input.StartDate.HasValue != Input.EndDate.HasValue || Input.EndDate < Input.StartDate)
            ModelState.AddModelError("Input.EndDate", L("Укажите обе даты; окончание не может быть раньше начала.", "Зазначте обидві дати; завершення не може бути раніше початку.", "Enter both dates; the end cannot precede the start."));
        if (Input.NextActionDate.HasValue && string.IsNullOrWhiteSpace(Input.NextAction))
            ModelState.AddModelError("Input.NextAction", L("Опишите действие для выбранной даты.", "Опишіть дію для обраної дати.", "Describe the action for the selected date."));
        if (!ModelState.IsValid) return Page();
        if (Input.Revision != inquiry.PilotRevision) return ConflictPage();
        inquiry.PilotStage = Input.Stage;
        inquiry.PilotNotes = Clean(Input.Notes);
        inquiry.NextAction = Clean(Input.NextAction);
        inquiry.NextActionDate = Input.NextActionDate;
        inquiry.PilotStartDate = Input.StartDate;
        inquiry.PilotEndDate = Input.EndDate;
        inquiry.ContactedAtUtc = Input.Stage == "new" ? null : inquiry.ContactedAtUtc ?? DateTime.UtcNow;
        inquiry.PilotRevision = Guid.NewGuid();
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return ConflictPage(); }
        Saved = true;
        return RedirectToPage(new { id });
    }

    private PageResult ConflictPage()
    {
        Response.StatusCode = StatusCodes.Status409Conflict;
        ModelState.AddModelError(string.Empty, L("Карточка уже изменена. Скопируйте свои заметки и обновите страницу перед повторным сохранением.", "Картку вже змінено. Скопіюйте свої нотатки й оновіть сторінку перед повторним збереженням.", "This card has changed. Copy your notes and reload the page before saving again."));
        return Page();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string L(string ru, string uk, string en) => AppLocalizer.Get(ru, uk, en);

    public sealed class PilotInput
    {
        [Required, StringLength(24)] public string Stage { get; set; } = "new";
        [StringLength(4000)] public string? Notes { get; set; }
        [StringLength(500)] public string? NextAction { get; set; }
        [DataType(DataType.Date)] public DateOnly? NextActionDate { get; set; }
        [DataType(DataType.Date)] public DateOnly? StartDate { get; set; }
        [DataType(DataType.Date)] public DateOnly? EndDate { get; set; }
        public Guid Revision { get; set; }
    }
}
