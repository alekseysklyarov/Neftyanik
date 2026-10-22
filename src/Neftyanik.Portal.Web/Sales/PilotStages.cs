using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Sales;

public static class PilotStages
{
    public static IReadOnlyList<string> Values { get; } = Array.AsReadOnly(new[]
        { "new", "contacted", "demo-scheduled", "demo-completed", "pilot", "completed", "declined", "deferred" });

    public static string Label(string stage) => stage switch
    {
        "contacted" => AppLocalizer.Get("Связались", "Зв’язалися", "Contacted"),
        "demo-scheduled" => AppLocalizer.Get("Показ назначен", "Показ призначено", "Demo scheduled"),
        "demo-completed" => AppLocalizer.Get("Показ проведён", "Показ проведено", "Demo completed"),
        "pilot" => AppLocalizer.Get("Пилот идёт", "Пілот триває", "Pilot active"),
        "completed" => AppLocalizer.Get("Пилот завершён", "Пілот завершено", "Pilot completed"),
        "declined" => AppLocalizer.Get("Отказ", "Відмова", "Declined"),
        "deferred" => AppLocalizer.Get("Вернуться позже", "Повернутися згодом", "Deferred"),
        _ => AppLocalizer.Get("Новая заявка", "Нова заявка", "New inquiry")
    };
}
