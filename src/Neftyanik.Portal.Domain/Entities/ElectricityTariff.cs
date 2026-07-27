namespace Neftyanik.Portal.Domain.Entities;

public class ElectricityTariff
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal SingleRatePrice { get; set; }

    public decimal? DayRatePrice { get; set; }

    public decimal? NightRatePrice { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public decimal Rate
    {
        get => SingleRatePrice;
        set => SingleRatePrice = value;
    }
}
