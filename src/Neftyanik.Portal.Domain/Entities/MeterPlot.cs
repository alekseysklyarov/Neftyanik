namespace Neftyanik.Portal.Domain.Entities;

public class MeterPlot
{
    public int MeterId { get; set; }

    public int PlotId { get; set; }

    public DateTimeOffset ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }

    public ElectricityMeter? Meter { get; set; }

    public Plot? Plot { get; set; }
}