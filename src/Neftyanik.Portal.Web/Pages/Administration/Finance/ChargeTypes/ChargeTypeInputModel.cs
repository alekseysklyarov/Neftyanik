using System.ComponentModel.DataAnnotations;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ChargeTypes;

public class ChargeTypeInputModel : IValidatableObject
{
    [Display(Name = "Наименование")]
    public string Name { get; set; } = string.Empty;

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
        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Укажите наименование типа начисления.", "Вкажіть назву типу нарахування.", "Enter the charge type name."),
                [nameof(Name)]);
        }
        else if (Name.Trim().Length > 150)
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Наименование не должно превышать 150 символов.", "Назва не повинна перевищувати 150 символів.", "The name must not exceed 150 characters."),
                [nameof(Name)]);
        }

        if (!string.IsNullOrWhiteSpace(Description) && Description.Trim().Length > 1000)
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Описание не должно превышать 1000 символов.", "Опис не повинен перевищувати 1000 символів.", "The description must not exceed 1000 characters."),
                [nameof(Description)]);
        }

        if (DefaultAmount.HasValue && DefaultAmount.Value <= 0m)
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Сумма по умолчанию должна быть больше нуля.", "Сума за замовчуванням має бути більшою за нуль.", "The default amount must be greater than zero."),
                [nameof(DefaultAmount)]);
        }

        if (IsYearly && OnlyOnOwnerChange)
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Тип начисления не может быть одновременно ежегодным и только при смене владельца.", "Тип нарахування не може бути одночасно щорічним і лише при зміні власника.", "A charge type cannot be both yearly and only on owner change."),
                [nameof(IsYearly), nameof(OnlyOnOwnerChange)]);
        }

        if (IsDefault && !IsActive)
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Тип начисления по умолчанию должен быть активным.", "Тип нарахування за замовчуванням повинен бути активним.", "A default charge type must be active."),
                [nameof(IsDefault), nameof(IsActive)]);
        }
    }
}
