using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Imports;
using static Neftyanik.Portal.Web.Imports.YearWorkbook;

namespace Neftyanik.Portal.Web.Pages.Administration.YearTransfer;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
[RequestSizeLimit(48 * 1024 * 1024)]
[RequestFormLimits(ValueLengthLimit = 32 * 1024 * 1024, MultipartBodyLengthLimit = 11 * 1024 * 1024)]
public class IndexModel(ApplicationDbContext db, IDataProtectionProvider protection, IFinancialAuditService audit,
    IMemberElectricityService memberElectricity, IAssociationElectricityService associationElectricity, ILogger<IndexModel> logger) : PageModel
{
    private readonly IDataProtector protector = protection.CreateProtector("DachaHub.YearTransfer.v1");
    private readonly YearTransferService transfer = new(db, audit, memberElectricity, associationElectricity);
    [BindProperty(SupportsGet = true)] public string Kind { get; set; } = YearWorkbook.Payments;
    [BindProperty(SupportsGet = true)] public string Mode { get; set; } = "import";
    [BindProperty(SupportsGet = true)] public int Year { get; set; } = DateTime.Today.Year;
    [BindProperty] public IFormFile? Upload { get; set; }
    [BindProperty] public string? PreviewToken { get; set; }
    public YearTransferResult? Preview { get; private set; }
    public string PageTitle => (Mode == "export" ? L("Экспорт: ", "Експорт: ", "Export: ") : L("Импорт: ", "Імпорт: ", "Import: ")) + Title(Kind);
    private bool SelectionValid()
    {
        Response.Headers.CacheControl = "no-store";
        if (!db.IsAssociationResolved || !ValidKind(Kind) || Mode is not ("import" or "export")) return false;
        if (!ValidYear(Year)) ModelState.AddModelError(nameof(Year), L("Выберите год от 2000 до 2100.", "Оберіть рік від 2000 до 2100.", "Select a year from 2000 through 2100."));
        return true;
    }
    public IActionResult OnGet() => SelectionValid() ? Page() : NotFound();
    public IActionResult OnGetTemplate()
    {
        if (!SelectionValid() || Mode != "import") return NotFound();
        return ModelState.IsValid ? Workbook(Write(Kind, Year, []), "template") : Page();
    }
    public async Task<IActionResult> OnPostDownloadAsync(CancellationToken ct)
    {
        if (!SelectionValid() || Mode != "export") return NotFound();
        if (!ModelState.IsValid) return Page();
        try { return Workbook(await transfer.ExportAsync(Kind, Year, ct), "export"); }
        catch (InvalidDataException ex) { ModelState.AddModelError(string.Empty, ex.Message); return Page(); }
    }
    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken ct)
    {
        if (!SelectionValid() || Mode != "import") return NotFound();
        PreviewToken = null; ModelState.Remove(nameof(PreviewToken));
        if (Upload is null || Upload.Length is 0 or > MaxBytes || !Path.GetExtension(Upload.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(string.Empty, L("Выберите файл .xlsx до 10 МБ.", "Оберіть файл .xlsx до 10 МБ.", "Choose an .xlsx file up to 10 MB."));
        if (!ModelState.IsValid) return Page();
        using var buffer = new MemoryStream(); await Upload!.CopyToAsync(buffer, ct);
        Preview = Read(buffer.ToArray(), Kind, Year);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        if (Preview.Issues.Count > 0) return Page();
        try { Preview = await transfer.ExecuteAsync(Kind, Year, Preview.Rows, userId, false, ct); }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException) { DatabaseError(ex); return Page(); }
        if (Preview.Issues.Count == 0)
        {
            var payload = JsonSerializer.Serialize(new Envelope(db.CurrentAssociationId, userId, Kind, Year, DateTimeOffset.UtcNow.AddMinutes(30), Preview.Rows));
            if (payload.Length > 16 * 1024 * 1024) ModelState.AddModelError(string.Empty, L("Слишком много текста. Разделите файл на части.", "Забагато тексту. Розділіть файл на частини.", "Too much text. Split the file into parts."));
            else PreviewToken = protector.Protect(payload);
        }
        return Page();
    }
    public async Task<IActionResult> OnPostConfirmAsync(CancellationToken ct)
    {
        if (!SelectionValid() || Mode != "import") return NotFound();
        Envelope? envelope = null;
        try
        {
            if (!string.IsNullOrEmpty(PreviewToken) && PreviewToken.Length <= 32 * 1024 * 1024)
                envelope = JsonSerializer.Deserialize<Envelope>(protector.Unprotect(PreviewToken));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException) { }
        PreviewToken = null; ModelState.Clear();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (envelope is null || string.IsNullOrEmpty(userId) || envelope.AssociationId != db.CurrentAssociationId || envelope.UserId != userId ||
            envelope.Kind != Kind || envelope.Year != Year || envelope.ExpiresAt < DateTimeOffset.UtcNow)
        {
            ModelState.AddModelError(string.Empty, L("Предпросмотр недействителен или устарел. Загрузите файл заново.", "Попередній перегляд недійсний або застарів. Завантажте файл знову.", "The preview is invalid or expired. Upload the file again.")); return Page();
        }
        try { Preview = await transfer.ExecuteAsync(Kind, Year, envelope.Rows, userId, true, ct); }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException) { DatabaseError(ex); return Page(); }
        if (Preview.Issues.Count > 0) return Page();
        TempData["SuccessMessage"] = L("Импорт завершён. Добавлено: ", "Імпорт завершено. Додано: ", "Import complete. Added: ") + Preview.Added +
            L("; уже существовали: ", "; вже існували: ", "; already existed: ") + Preview.Skipped + ".";
        return RedirectToPage(new { kind = Kind, mode = Mode, year = Year });
    }
    private FileContentResult Workbook(byte[] bytes, string purpose) => File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DachaHub-{Kind}-{Year}-{purpose}.xlsx");
    private void DatabaseError(Exception ex)
    {
        logger.LogWarning(ex, "Year transfer failed for association {AssociationId}, {Kind}, {Year}", db.CurrentAssociationId, Kind, Year);
        db.ChangeTracker.Clear();
        ModelState.AddModelError(string.Empty, L("Операция не выполнена. Данные могли измениться; обновите предпросмотр. Изменения не сохранены.", "Операцію не виконано. Дані могли змінитися; оновіть попередній перегляд. Зміни не збережено.", "Operation failed. Data may have changed; refresh the preview. No changes were saved."));
    }
    public sealed record Envelope(int AssociationId, string UserId, string Kind, int Year, DateTimeOffset ExpiresAt, List<YearTransferRow> Rows);
}
