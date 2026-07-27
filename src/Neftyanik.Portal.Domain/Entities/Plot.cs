namespace Neftyanik.Portal.Domain.Entities;

public enum PlotStatus
{
    Available = 0,
    Occupied = 1,
    Reserved = 2,
    Unavailable = 3
}

public class Plot
{
    public int Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public decimal? AreaSquareMeters { get; set; }

    public string? Address { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ClosedAt { get; set; }

    // Owner navigation is provided by Infrastructure layer

    public List<PlotOwnershipHistory> OwnershipHistory { get; set; } = new List<PlotOwnershipHistory>();

    public List<MeterPlot> MeterPlots { get; set; } = new List<MeterPlot>();

    public List<Charge> Charges { get; set; } = new List<Charge>();

    public decimal Area
    {
        get => AreaSquareMeters ?? 0m;
        set => AreaSquareMeters = value;
    }

    public PlotStatus Status
    {
        get => IsActive ? PlotStatus.Occupied : PlotStatus.Unavailable;
        set => IsActive = value != PlotStatus.Unavailable;
    }
}
