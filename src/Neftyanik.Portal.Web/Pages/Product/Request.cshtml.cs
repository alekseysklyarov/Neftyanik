using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Product;

[AllowAnonymous]
[EnableRateLimiting("DemoRequests")]
[RequestSizeLimit(16384)]
public class RequestModel(ApplicationDbContext database, ILogger<RequestModel> logger) : PageModel
{
    [BindProperty] public RequestInput Input { get; set; } = new();
    [BindProperty, StringLength(200)] public string? Website { get; set; }
    [TempData] public bool RequestSubmitted { get; set; }

    public void OnGet() => Response.Headers.CacheControl = "no-store";

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!string.IsNullOrEmpty(Website)) return BadRequest();
        LocalizeValidationErrors();
        if (!Input.Consent)
            ModelState.AddModelError("Input.Consent", AppLocalizer.Get("Подтвердите согласие на ответ по заявке.", "Підтвердьте згоду на відповідь за заявкою.", "Please consent to a reply about this request."));
        if (!ModelState.IsValid) return Page();

        database.DemoRequests.Add(new DemoRequest
        {
            ContactName = Input.ContactName.Trim(), AssociationName = Input.AssociationName.Trim(),
            Email = Input.Email.Trim(), PlotCount = Input.PlotCount,
            Message = string.IsNullOrWhiteSpace(Input.Message) ? null : Input.Message.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            logger.LogError("A demonstration request could not be saved.");
            ModelState.AddModelError(string.Empty, AppLocalizer.Get("Не удалось сохранить заявку. Попробуйте ещё раз позже.", "Не вдалося зберегти заявку. Спробуйте ще раз пізніше.", "Your request could not be saved. Please try again later."));
            return Page();
        }
        RequestSubmitted = true;
        return RedirectToPage(new { culture = System.Globalization.CultureInfo.CurrentUICulture.Name });
    }

    private void LocalizeValidationErrors()
    {
        var messages = new Dictionary<string, string>
        {
            ["Input.ContactName"] = AppLocalizer.Get("Укажите имя длиной до 120 символов.", "Зазначте ім’я довжиною до 120 символів.", "Enter a name of up to 120 characters."),
            ["Input.AssociationName"] = AppLocalizer.Get("Укажите название товарищества длиной до 200 символов.", "Зазначте назву товариства довжиною до 200 символів.", "Enter an association name of up to 200 characters."),
            ["Input.Email"] = AppLocalizer.Get("Укажите корректный email длиной до 254 символов.", "Зазначте коректний email довжиною до 254 символів.", "Enter a valid email of up to 254 characters."),
            ["Input.PlotCount"] = AppLocalizer.Get("Количество участков: целое число от 1 до 100000.", "Кількість ділянок: ціле число від 1 до 100000.", "Plot count must be a whole number from 1 to 100000."),
            ["Input.Message"] = AppLocalizer.Get("Сократите сообщение до 1000 символов.", "Скоротіть повідомлення до 1000 символів.", "Keep the message within 1,000 characters.")
        };
        foreach (var (key, message) in messages)
            if (ModelState.TryGetValue(key, out var state) && state.Errors.Count > 0)
            {
                state.Errors.Clear();
                state.Errors.Add(message);
            }
    }

    public sealed class RequestInput
    {
        [Required, StringLength(120)] public string ContactName { get; set; } = string.Empty;
        [Required, StringLength(200)] public string AssociationName { get; set; } = string.Empty;
        [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = string.Empty;
        [Range(1, 100000)] public int? PlotCount { get; set; }
        [StringLength(1000)] public string? Message { get; set; }
        public bool Consent { get; set; }
    }
}
