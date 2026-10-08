using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Finance;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Settings;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class PaymentInstructionsModel(ApplicationDbContext db, IFinancialAuditService audit) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public async Task OnGetAsync(CancellationToken ct)
    {
        var data = await PaymentInstructionsData.LoadAsync(db, ct);
        Input = new() { Recipient = data.Recipient, CardNumber = data.CardNumber, Purpose = data.Purpose, Contact = data.Contact };
    }
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Input.CardNumber = string.Concat((Input.CardNumber ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-'));
        if (!ValidCardNumber(Input.CardNumber)) ModelState.AddModelError("Input.CardNumber", "Проверьте номер карты: требуется от 13 до 19 цифр с правильной контрольной цифрой.");
        if (!ModelState.IsValid) return Page();
        var data = new PaymentInstructionsData(Input.Recipient.Trim(), Input.CardNumber, Input.Purpose.Trim(), Input.Contact?.Trim() ?? "");
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        if (json.Length > 2000)
        {
            ModelState.AddModelError(string.Empty, "Сократите реквизиты и инструкцию до 2000 символов.");
            return Page();
        }
        var setting = await db.SystemSettings.SingleOrDefaultAsync(s => s.Key == PaymentInstructionsData.SettingKey, ct);
        var old = setting?.Value;
        if (setting is null) { setting = new() { Key = PaymentInstructionsData.SettingKey }; db.SystemSettings.Add(setting); }
        setting.Value = json;
        setting.UpdatedAt = DateTimeOffset.UtcNow;
        setting.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        audit.Add(FinancialAuditLogActions.Updated, nameof(SystemSetting), PaymentInstructionsData.SettingKey,
            "Обновлены реквизиты для оплаты участниками.", old, data);
        await db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Реквизиты сохранены и доступны участникам.";
        return RedirectToPage();
    }
    internal static bool ValidCardNumber(string number)
    {
        if (number.Length is < 13 or > 19 || number.Any(c => !char.IsAsciiDigit(c)) || number.All(c => c == '0')) return false;
        var sum = 0;
        var doubleDigit = false;
        for (var i = number.Length - 1; i >= 0; i--)
        {
            var digit = number[i] - '0';
            if (doubleDigit) { digit *= 2; if (digit > 9) digit -= 9; }
            sum += digit;
            doubleDigit = !doubleDigit;
        }
        return sum % 10 == 0;
    }
    public sealed class InputModel
    {
        [Required, StringLength(200), Display(Name = "Получатель")] public string Recipient { get; set; } = "";
        [Required, StringLength(42), Display(Name = "Номер карты")] public string CardNumber { get; set; } = "";
        [Required, StringLength(500), Display(Name = "Инструкция по назначению платежа")] public string Purpose { get; set; } = "";
        [StringLength(200), Display(Name = "Контакт бухгалтера")] public string? Contact { get; set; }
    }
}
