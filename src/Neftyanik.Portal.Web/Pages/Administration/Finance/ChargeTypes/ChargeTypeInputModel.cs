using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ChargeTypes;

public class ChargeTypeInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Укажите наименование типа начисления.")]
    [StringLength(150, ErrorMessage = "Наименование не должно превышать 150 символов.")]
    [Display(Name = "Наименование")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Описание не должно превышать 1000 символов.")]
    [Display(Name = "Описание")]
    public string? Description { get; set; }

    [Display(Name = "Сумма по умолчанию")]
    public decimal? DefaultAmount { get; set; }

    [Display(Name = "Активен")]
    public bool IsActive { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DefaultAmount.HasValue && DefaultAmount.Value <= 0m)
        {
            yield return new ValidationResult(
                "Сумма по умолчанию должна быть больше нуля.",
                [nameof(DefaultAmount)]);
        }
    }
}
