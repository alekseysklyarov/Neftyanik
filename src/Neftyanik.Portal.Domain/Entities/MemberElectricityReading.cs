namespace Neftyanik.Portal.Domain.Entities;

public class MemberElectricityReading
{
    public long Id { get; set; }

    public int MemberElectricityMeterId { get; set; }

    public MemberElectricityMeter? MemberElectricityMeter { get; set; }

    public DateOnly ReadingDate { get; set; }

    public decimal CurrentReading { get; set; }

    public decimal? CurrentNightReading { get; set; }

    public decimal? AppliedMemberRate { get; set; }

    public decimal? AppliedMemberNightRate { get; set; }

    public decimal? Amount { get; set; }

    public bool IsInitialReading { get; set; }

    public long? ChargeId { get; set; }

    public Charge? Charge { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? CreatedByUserId { get; set; }

    public ApplicationUser? CreatedByUser { get; set; }

    public bool SubmittedByMember { get; set; }
}
