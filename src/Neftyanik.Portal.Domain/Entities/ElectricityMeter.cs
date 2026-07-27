using Neftyanik.Portal.Domain.Enums;

namespace Neftyanik.Portal.Domain.Entities;

public class ElectricityMeter
{
    public int Id { get; set; }

    public string SerialNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public MeterKind MeterKind { get; set; }

    public TariffMode TariffMode { get; set; }

    public bool IsActive { get; set; } = true;

    public DateOnly? InstallationDate { get; set; }

    public decimal InitialReading { get; set; }

    public decimal? InitialDayReading { get; set; }

    public decimal? InitialNightReading { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? OwnerId { get; set; }

    // Owner navigation should be provided by Infrastructure layer

    public List<MeterPlot> MeterPlots { get; set; } = new List<MeterPlot>();

    public List<MeterReading> MeterReadings { get; set; } = new List<MeterReading>();

    public List<Charge> Charges { get; set; } = new List<Charge>();
}
