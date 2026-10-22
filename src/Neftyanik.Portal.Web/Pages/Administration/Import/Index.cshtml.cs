using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Imports;
using static Neftyanik.Portal.Web.Imports.ExcelOnboarding;

namespace Neftyanik.Portal.Web.Pages.Administration.Import;

[Authorize(Roles = RoleNames.Administrator)]
[RequestSizeLimit(8 * 1024 * 1024)]
[RequestFormLimits(ValueLengthLimit = 4 * 1024 * 1024, MultipartBodyLengthLimit = 6 * 1024 * 1024)]
public class IndexModel(ApplicationDbContext db, IDataProtectionProvider protection, ILogger<IndexModel> logger) : PageModel
{
    private readonly IDataProtector protector = protection.CreateProtector("DachaHub.ExcelOnboarding.v1");
    [BindProperty] public IFormFile? Upload { get; set; }
    [BindProperty] public DateOnly TransferDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [BindProperty] public string? PreviewToken { get; set; }
    public OnboardingPreview? Preview { get; private set; }
    public IActionResult OnGet()
    {
        Response.Headers.CacheControl = "no-store";
        return db.IsAssociationResolved ? Page() : NotFound();
    }
    public IActionResult OnGetTemplate()
    {
        Response.Headers.CacheControl = "no-store";
        return db.IsAssociationResolved ? File(Template(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "DachaHub-import.xlsx") : NotFound();
    }
    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        PreviewToken = null;
        ModelState.Remove(nameof(PreviewToken));
        if (!db.IsAssociationResolved) return NotFound();
        if (Upload is null || Upload.Length == 0 || Upload.Length > MaxBytes || !Path.GetExtension(Upload.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(string.Empty, L("Выберите файл .xlsx размером до 5 МБ.", "Оберіть файл .xlsx розміром до 5 МБ.", "Choose an .xlsx file up to 5 MB."));
        if (!ModelState.IsValid) return Page();
        using var buffer = new MemoryStream();
        await Upload!.CopyToAsync(buffer, ct);
        Preview = Parse(buffer.ToArray());
        await ValidateAsync(db, Preview, TransferDate, ct);
        if (Preview.Issues.Count == 0)
        {
            PreviewToken = protector.Protect(JsonSerializer.Serialize(new PreviewEnvelope(db.CurrentAssociationId,
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", DateTimeOffset.UtcNow.AddMinutes(30), TransferDate, Preview.Rows)));
        }
        return Page();
    }
    public async Task<IActionResult> OnPostConfirmAsync(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!db.IsAssociationResolved) return NotFound();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        PreviewEnvelope? envelope = null;
        try
        {
            if (!string.IsNullOrEmpty(PreviewToken) && PreviewToken.Length <= 4 * 1024 * 1024)
                envelope = JsonSerializer.Deserialize<PreviewEnvelope>(protector.Unprotect(PreviewToken));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException) { }
        PreviewToken = null;
        ModelState.Clear();
        if (envelope is null || string.IsNullOrEmpty(userId) || envelope.UserId != userId ||
            envelope.AssociationId != db.CurrentAssociationId || envelope.ExpiresAt < DateTimeOffset.UtcNow)
        {
            ModelState.AddModelError(string.Empty, L("Предпросмотр устарел или недействителен. Загрузите файл заново.", "Попередній перегляд застарів або недійсний. Завантажте файл знову.", "The preview expired or is invalid. Upload the file again."));
            return Page();
        }
        TransferDate = envelope.TransferDate;
        try
        {
            Preview = await SaveAsync(db, envelope.Rows, TransferDate, userId, ct);
        }
        catch (Exception ex) when (ex is DbUpdateException || ex is System.Data.Common.DbException)
        {
            logger.LogWarning(ex, "Excel onboarding failed for association {AssociationId}", db.CurrentAssociationId);
            db.ChangeTracker.Clear();
            ModelState.AddModelError(string.Empty, L("Импорт не выполнен. Возможно, данные изменились. Обновите предпросмотр и повторите попытку.", "Імпорт не виконано. Можливо, дані змінилися. Оновіть попередній перегляд і повторіть спробу.", "Import failed. Data may have changed. Refresh the preview and try again."));
            return Page();
        }
        if (Preview.Issues.Count > 0) return Page();
        TempData["SuccessMessage"] = L("Импорт завершён. Участников: ", "Імпорт завершено. Учасників: ", "Import complete. Members: ") + Preview.Members +
            L("; участков: ", "; ділянок: ", "; plots: ") + Preview.Plots + ".";
        return RedirectToPage();
    }
    public sealed record PreviewEnvelope(int AssociationId, string UserId, DateTimeOffset ExpiresAt, DateOnly TransferDate, List<OnboardingRow> Rows);
}
