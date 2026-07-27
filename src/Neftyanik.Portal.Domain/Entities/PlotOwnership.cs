using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Domain.Entities;

public class PlotOwnership : IValidatableObject
{
    public int Id { get; set; }

    public int PlotId { get; set; }

    public Plot? Plot { get; set; }

    public int MemberId { get; set; }

    public Member? Member { get; set; }

    [Range(typeof(decimal), "0.01", "100")]
    public decimal? OwnershipShare { get; set; }

    public bool IsPrimaryContact { get; set; }

    public DateOnly? ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OwnershipShare.HasValue && (OwnershipShare.Value <= 0m || OwnershipShare.Value > 100m))
        {
            yield return new ValidationResult(
                "OwnershipShare must be greater than 0 and no more than 100.",
                [nameof(OwnershipShare)]);
        }

        if (ValidFrom.HasValue && ValidTo.HasValue && ValidTo.Value < ValidFrom.Value)
        {
            yield return new ValidationResult(
                "ValidTo must not be earlier than ValidFrom.",
                [nameof(ValidFrom), nameof(ValidTo)]);
        }
    }
}
