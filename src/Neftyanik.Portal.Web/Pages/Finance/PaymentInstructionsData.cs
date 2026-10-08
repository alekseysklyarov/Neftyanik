using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Finance;

public sealed record PaymentInstructionsData(string Recipient = "", string CardNumber = "", string Purpose = "", string Contact = "")
{
    public const string SettingKey = "Finance.PaymentInstructions";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Recipient) && !string.IsNullOrWhiteSpace(CardNumber);
    public static async Task<PaymentInstructionsData> LoadAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var json = await db.SystemSettings.AsNoTracking().Where(s => s.Key == SettingKey).Select(s => s.Value).SingleOrDefaultAsync(ct);
        try { return json is null ? new() : JsonSerializer.Deserialize<PaymentInstructionsData>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}
