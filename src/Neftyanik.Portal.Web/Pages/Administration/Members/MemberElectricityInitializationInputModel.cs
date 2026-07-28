using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

public class MemberElectricityInitializationInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Выберите счётчик.")]
    [Display(Name = "Счётчик")]
    public int? MeterId { get; set; }

    [Required(ErrorMessage = "Укажите дату инициализации.")]
    [DataType(DataType.Date)]
    [Display(Name = "Дата инициализации")]
    public DateOnly? ReadingDate { get; set; }

    [Required(ErrorMessage = "Укажите текущее показание.")]
    [Display(Name = "Текущее показание")]
    public decimal? CurrentReading { get; set; }

    [Display(Name = "Текущая задолженность по электроэнергии, грн")]
    public decimal? OpeningDebtAmount { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CurrentReading.HasValue && CurrentReading.Value < 0m)
        {
            yield return new ValidationResult("Показание не может быть отрицательным.", [nameof(CurrentReading)]);
        }

        if (OpeningDebtAmount.HasValue && OpeningDebtAmount.Value < 0m)
        {
            yield return new ValidationResult("Задолженность не может быть отрицательной.", [nameof(OpeningDebtAmount)]);
        }
    }
}
