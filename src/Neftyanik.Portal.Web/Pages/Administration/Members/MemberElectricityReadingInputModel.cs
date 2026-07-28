using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

public class MemberElectricityReadingInputModel
    : IValidatableObject
{
    [Required(ErrorMessage = "Выберите счётчик.")]
    [Display(Name = "Счётчик")]
    public int? MeterId { get; set; }

    [Required(ErrorMessage = "Укажите дату показаний.")]
    [DataType(DataType.Date)]
    [Display(Name = "Дата показаний")]
    public DateOnly? ReadingDate { get; set; }

    [Required(ErrorMessage = "Укажите текущее показание.")]
    [Display(Name = "Текущее показание")]
    public decimal? CurrentReading { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CurrentReading.HasValue && CurrentReading.Value < 0m)
        {
            yield return new ValidationResult("Показание не может быть отрицательным.", [nameof(CurrentReading)]);
        }
    }
}
