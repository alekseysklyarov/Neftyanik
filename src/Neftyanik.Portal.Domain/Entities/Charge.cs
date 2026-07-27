using Neftyanik.Portal.Domain.Enums;

namespace Neftyanik.Portal.Domain.Entities;

public class Charge
{
    public long Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int? PlotId { get; set; }

    public int? MeterId { get; set; }

    public ChargeType ChargeType { get; set; }

    public int PeriodYear { get; set; }

    public int? PeriodMonth { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal? Quantity { get; set; }

    public decimal? UnitPrice { get; set; }

    public decimal Amount { get; set; }

    public DateTimeOffset ChargedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateOnly? DueDate { get; set; }

    public ChargeStatus Status { get; set; } = ChargeStatus.Active;

    public long? SourceReadingId { get; set; }

    public string? CreatedByUserId { get; set; }

    // User navigation is stored as UserId to avoid domain->infrastructure dependency
    public Plot? Plot { get; set; }

    public ElectricityMeter? Meter { get; set; }

    public MeterReading? SourceReading { get; set; }

    public List<PaymentAllocation> PaymentAllocations { get; set; } = new List<PaymentAllocation>();
}