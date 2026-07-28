using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Electricity;

public class InitialElectricityReadingInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Укажите дату показаний.")]
    [DataType(DataType.Date)]
    [Display(Name = "Дата показаний")]
    public DateOnly? ReadingDate { get; set; }

    [Required(ErrorMessage = "Укажите дневное показание.")]
    [Display(Name = "Дневное показание, кВт·ч")]
    public decimal? CurrentDayReading { get; set; }

    [Required(ErrorMessage = "Укажите ночное показание.")]
    [Display(Name = "Ночное показание, кВт·ч")]
    public decimal? CurrentNightReading { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CurrentDayReading.HasValue && CurrentDayReading.Value < 0m)
        {
            yield return new ValidationResult("Дневное показание не может быть отрицательным.", [nameof(CurrentDayReading)]);
        }

        if (CurrentNightReading.HasValue && CurrentNightReading.Value < 0m)
        {
            yield return new ValidationResult("Ночное показание не может быть отрицательным.", [nameof(CurrentNightReading)]);
        }
    }
}

public class ElectricityReadingInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Укажите дату новых показаний.")]
    [DataType(DataType.Date)]
    [Display(Name = "Дата новых показаний")]
    public DateOnly? ReadingDate { get; set; }

    [Required(ErrorMessage = "Укажите текущее дневное показание.")]
    [Display(Name = "Текущее дневное показание, кВт·ч")]
    public decimal? CurrentDayReading { get; set; }

    [Required(ErrorMessage = "Укажите текущее ночное показание.")]
    [Display(Name = "Текущее ночное показание, кВт·ч")]
    public decimal? CurrentNightReading { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CurrentDayReading.HasValue && CurrentDayReading.Value < 0m)
        {
            yield return new ValidationResult("Текущее дневное показание не может быть отрицательным.", [nameof(CurrentDayReading)]);
        }

        if (CurrentNightReading.HasValue && CurrentNightReading.Value < 0m)
        {
            yield return new ValidationResult("Текущее ночное показание не может быть отрицательным.", [nameof(CurrentNightReading)]);
        }
    }
}
