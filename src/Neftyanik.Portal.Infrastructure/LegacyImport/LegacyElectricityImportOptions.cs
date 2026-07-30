namespace Neftyanik.Portal.Infrastructure.LegacyImport;

public sealed class LegacyElectricityImportOptions
{
    public const string SectionName = "LegacyElectricityImport";

    public string WorkbookRelativePath { get; set; } = "src/Neftyanik.Portal.Web/App_Data/Legacy/Оплата электроэнергии.xlsx";

    public string ReportsRelativePath { get; set; } = "src/Neftyanik.Portal.Web/App_Data/Legacy/Reports";

    public DateOnly DefaultPreviousReadingDate { get; set; } = new(2025, 6, 1);

    public DateOnly DefaultCurrentReadingDate { get; set; } = new(2025, 7, 1);

    public DateOnly OwnershipEffectiveFrom { get; set; } = new(2025, 7, 1);
}
