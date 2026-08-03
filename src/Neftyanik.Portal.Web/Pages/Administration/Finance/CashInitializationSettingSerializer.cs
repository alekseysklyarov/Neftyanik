using System.Text.Json;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance;

internal static class CashInitializationSettingSerializer
{
    public const string SettingKey = "Finance.CashInitialization";
    public const string SettingDescription = "Initial cash amount configured from finance settings.";

    public static string Serialize(CashInitializationSettingData value)
    {
        return JsonSerializer.Serialize(value);
    }

    public static CashInitializationSettingData? Deserialize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CashInitializationSettingData>(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public sealed record CashInitializationSettingData(
        decimal Amount,
        DateOnly AcceptedAt,
        string AcceptedFrom,
        decimal AdvancePaymentsAmount = 0m);
}
