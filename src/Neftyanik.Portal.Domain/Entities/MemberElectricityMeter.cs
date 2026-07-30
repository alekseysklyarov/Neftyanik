namespace Neftyanik.Portal.Domain.Entities;

public class MemberElectricityMeter
{
    public int Id { get; set; }

    public int MemberId { get; set; }

    public Member? Member { get; set; }

    public string? MeterNumber { get; set; }

    public string? Name { get; set; }

    public bool IsActive { get; set; } = true;

    public int BillingPlotId { get; set; }

    public Plot? BillingPlot { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? CreatedByUserId { get; set; }

    public ApplicationUser? CreatedByUser { get; set; }

    public List<Plot> Plots { get; set; } = [];

    public List<MemberElectricityReading> Readings { get; set; } = [];
}
