namespace Neftyanik.Portal.Domain.Entities;

public class PaymentAllocation
{
    public long Id { get; set; }

    public long PaymentId { get; set; }

    public long ChargeId { get; set; }

    public decimal Amount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Payment? Payment { get; set; }

    public Charge? Charge { get; set; }
}