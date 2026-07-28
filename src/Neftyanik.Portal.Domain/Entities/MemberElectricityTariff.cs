namespace Neftyanik.Portal.Domain.Entities;

public class MemberElectricityTariff
{
    public int Id { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public decimal Rate { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? CreatedByUserId { get; set; }

    public ApplicationUser? CreatedByUser { get; set; }
}
