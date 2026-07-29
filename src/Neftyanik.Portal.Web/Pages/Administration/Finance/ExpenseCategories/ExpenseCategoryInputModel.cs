using System.ComponentModel.DataAnnotations;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ExpenseCategories;

public class ExpenseCategoryInputModel : IValidatableObject
{
    [Display(Name = "Наименование")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Описание")]
    public string? Description { get; set; }

    [Display(Name = "Активен")]
    public bool IsActive { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Укажите наименование типа расхода.", "Вкажіть назву типу витрати.", "Enter the expense type name."),
                [nameof(Name)]);
        }
        else if (Name.Trim().Length > 200)
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Наименование не должно превышать 200 символов.", "Назва не повинна перевищувати 200 символів.", "The name must not exceed 200 characters."),
                [nameof(Name)]);
        }

        if (!string.IsNullOrWhiteSpace(Description) && Description.Trim().Length > 1000)
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Описание не должно превышать 1000 символов.", "Опис не повинен перевищувати 1000 символів.", "The description must not exceed 1000 characters."),
                [nameof(Description)]);
        }
    }
}
