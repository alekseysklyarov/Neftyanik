using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Domain.Entities;

public class Charge : IValidatableObject
{
    public long Id { get; set; }

    public int? PlotId { get; set; }

    public Plot? Plot { get; set; }

    public int ChargeTypeId { get; set; }

    public ChargeType? ChargeType { get; set; }

    public decimal Amount { get; set; }

    public DateOnly ChargeDate { get; set; }

    public DateOnly? DueDate { get; set; }

    public int? PeriodYear { get; set; }

    public int? PeriodMonth { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string? CreatedByUserId { get; set; }

    public ApplicationUser? CreatedByUser { get; set; }

    public MemberElectricityReading? MemberElectricityReading { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    [StringLength(500)]
    public string? CancellationReason { get; set; }

    public List<PaymentAllocation> PaymentAllocations { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount < 0m)
        {
            yield return new ValidationResult("Charge amount cannot be negative.", [nameof(Amount)]);
        }

        if (DueDate.HasValue && DueDate.Value < ChargeDate)
        {
            yield return new ValidationResult("Due date cannot be earlier than charge date.", [nameof(DueDate), nameof(ChargeDate)]);
        }

        if (PeriodMonth.HasValue && (PeriodMonth.Value < 1 || PeriodMonth.Value > 12))
        {
            yield return new ValidationResult("Period month must be between 1 and 12.", [nameof(PeriodMonth)]);
        }

        if (PeriodYear.HasValue && (PeriodYear.Value < 2000 || PeriodYear.Value > 2100))
        {
            yield return new ValidationResult("Period year must be between 2000 and 2100.", [nameof(PeriodYear)]);
        }
    }
}
