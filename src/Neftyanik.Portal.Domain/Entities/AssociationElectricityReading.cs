namespace Neftyanik.Portal.Domain.Entities;

public class AssociationElectricityReading
{
    public long Id { get; set; }

    public DateOnly ReadingDate { get; set; }

    public decimal? PreviousDayReading { get; set; }

    public decimal CurrentDayReading { get; set; }

    public decimal? DayConsumption { get; set; }

    public decimal? AppliedSupplierDayRate { get; set; }

    public decimal? DayAmount { get; set; }

    public decimal? PreviousNightReading { get; set; }

    public decimal CurrentNightReading { get; set; }

    public decimal? NightConsumption { get; set; }

    public decimal? AppliedSupplierNightRate { get; set; }

    public decimal? NightAmount { get; set; }

    public decimal? TotalConsumption { get; set; }

    public decimal? TotalSupplierAmount { get; set; }

    public bool IsInitialReading { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? CreatedByUserId { get; set; }

    public ApplicationUser? CreatedByUser { get; set; }
}
