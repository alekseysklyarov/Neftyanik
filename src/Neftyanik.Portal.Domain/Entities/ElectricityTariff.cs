namespace Neftyanik.Portal.Domain.Entities;

public class ElectricityTariff
{
    public int Id { get; set; }

    public decimal DayRate { get; set; }

    public decimal NightRate { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? CreatedByUserId { get; set; }

    public ApplicationUser? CreatedByUser { get; set; }
}
