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

    [Display(Name = "По умолчанию")]
    public bool IsDefault { get; set; }

    [Display(Name = "Ежегодный")]
    public bool IsYearly { get; set; }

    [Display(Name = "Только при смене владельца")]
    public bool OnlyOnOwnerChange { get; set; }

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

        if (IsYearly && OnlyOnOwnerChange)
        {
            yield return new ValidationResult(
                "Тип начисления не может быть одновременно ежегодным и только при смене владельца.",
                [nameof(IsYearly), nameof(OnlyOnOwnerChange)]);
        }

        if (IsDefault && !IsActive)
        {
            yield return new ValidationResult(
                "Тип начисления по умолчанию должен быть активным.",
                [nameof(IsDefault), nameof(IsActive)]);
        }
    }
}
