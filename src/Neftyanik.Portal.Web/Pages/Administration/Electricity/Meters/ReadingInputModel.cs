using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters;

public class ReadingInputModel
    : IValidatableObject
{
    [Required(ErrorMessage = "Укажите дату показаний.")]
    [DataType(DataType.Date)]
    public DateOnly? ReadingDate { get; set; }

    [Required(ErrorMessage = "Укажите текущее показание.")]
    public decimal? CurrentReading { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CurrentReading.HasValue && CurrentReading.Value < 0m)
        {
            yield return new ValidationResult("Показание не может быть отрицательным.", [nameof(CurrentReading)]);
        }
    }
}
