using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Electricity.Tariffs;

public class TariffInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Укажите дату начала действия тарифа.")]
    [DataType(DataType.Date)]
    [Display(Name = "Действует с")]
    public DateOnly? EffectiveFrom { get; set; }

    [Required(ErrorMessage = "Укажите дневной тариф.")]
    [Display(Name = "Дневной тариф, грн/кВт·ч")]
    public decimal? DayRate { get; set; }

    [Required(ErrorMessage = "Укажите ночной тариф.")]
    [Display(Name = "Ночной тариф, грн/кВт·ч")]
    public decimal? NightRate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DayRate.HasValue && DayRate.Value < 0m)
        {
            yield return new ValidationResult("Дневной тариф не может быть отрицательным.", [nameof(DayRate)]);
        }

        if (NightRate.HasValue && NightRate.Value < 0m)
        {
            yield return new ValidationResult("Ночной тариф не может быть отрицательным.", [nameof(NightRate)]);
        }
    }
}
