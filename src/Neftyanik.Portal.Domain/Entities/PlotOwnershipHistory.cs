namespace Neftyanik.Portal.Domain.Entities;

public class PlotOwnershipHistory
{
    public long Id { get; set; }

    public int PlotId { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    public DateTimeOffset ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }

    public string? Comment { get; set; }

    public Plot? Plot { get; set; }

    // Owner navigation is provided by the Infrastructure layer
}