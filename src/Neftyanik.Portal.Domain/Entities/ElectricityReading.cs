namespace Neftyanik.Portal.Domain.Entities;

public class ElectricityReading
{
    public long Id { get; set; }

    public int PlotId { get; set; }

    public Plot? Plot { get; set; }

    public DateOnly ReadingDate { get; set; }

    public decimal? PreviousDayReading { get; set; }

    public decimal CurrentDayReading { get; set; }

    public decimal? DayConsumption { get; set; }

    public decimal? DayRate { get; set; }

    public decimal? DayAmount { get; set; }

    public decimal? PreviousNightReading { get; set; }

    public decimal CurrentNightReading { get; set; }

    public decimal? NightConsumption { get; set; }

    public decimal? NightRate { get; set; }

    public decimal? NightAmount { get; set; }

    public decimal? TotalAmount { get; set; }

    public bool IsInitialReading { get; set; }

    public long? ChargeId { get; set; }

    public Charge? Charge { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? CreatedByUserId { get; set; }

    public ApplicationUser? CreatedByUser { get; set; }
}
