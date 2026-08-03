using Neftyanik.Portal.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Domain.Entities;

public class Payment : IValidatableObject
{
    public long Id { get; set; }

    public int? PlotId { get; set; }

    public Plot? Plot { get; set; }

    public DateOnly PaymentDate { get; set; }

    public decimal Amount { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public string? ReferenceNumber { get; set; }

    public string? Description { get; set; }

    public string? CreatedByUserId { get; set; }

    public ApplicationUser? CreatedByUser { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CancelledAtUtc { get; set; }

    public string? CancellationReason { get; set; }

    public List<PaymentAllocation> PaymentAllocations { get; set; } = [];

    public PaymentNotification? PaymentNotification { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount <= 0m)
        {
            yield return new ValidationResult("Payment amount must be greater than zero.", [nameof(Amount)]);
        }
    }
}
