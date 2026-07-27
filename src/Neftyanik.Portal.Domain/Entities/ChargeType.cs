using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Domain.Entities;

public class ChargeType : IValidatableObject
{
    public int Id { get; set; }

    [Required]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public decimal? DefaultAmount { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public List<Charge> Charges { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DefaultAmount.HasValue && DefaultAmount.Value <= 0m)
        {
            yield return new ValidationResult(
                "DefaultAmount must be greater than zero when specified.",
                [nameof(DefaultAmount)]);
        }
    }
}
