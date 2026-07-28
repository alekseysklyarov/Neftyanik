using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Ownerships;

public class OwnershipInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Выберите члена товарищества.")]
    [Display(Name = "Член товарищества")]
    public int? MemberId { get; set; }

    [Display(Name = "Доля владения, %")]
    public decimal? OwnershipShare { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Действует с")]
    public DateOnly? ValidFrom { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OwnershipShare.HasValue && (OwnershipShare.Value <= 0m || OwnershipShare.Value > 100m))
        {
            yield return new ValidationResult(
                "Доля владения должна быть больше 0 и не больше 100.",
                [nameof(OwnershipShare)]);
        }
    }
}
