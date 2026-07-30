using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

public class MemberElectricitySetupInputModel : IValidatableObject
{
    [StringLength(100)]
    [Display(Name = "Номер счётчика")]
    public string? MeterNumber { get; set; }

    [StringLength(200)]
    [Display(Name = "Название счётчика")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Выберите расчётный участок.")]
    [Display(Name = "Расчётный участок")]
    public int? BillingPlotId { get; set; }

    [Required(ErrorMessage = "Укажите дату инициализации.")]
    [DataType(DataType.Date)]
    [Display(Name = "Дата инициализации")]
    public DateOnly? ReadingDate { get; set; }

    [Required(ErrorMessage = "Укажите текущее показание.")]
    [Display(Name = "Текущее показание")]
    public decimal? CurrentReading { get; set; }

    [Display(Name = "Ночное показание")]
    public decimal? CurrentNightReading { get; set; }

    [Display(Name = "Начальная задолженность по электроэнергии, грн")]
    public decimal? OpeningDebtAmount { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CurrentReading.HasValue && CurrentReading.Value < 0m)
        {
            yield return new ValidationResult("Показание не может быть отрицательным.", [nameof(CurrentReading)]);
        }

        if (CurrentNightReading.HasValue && CurrentNightReading.Value < 0m)
        {
            yield return new ValidationResult("Ночное показание не может быть отрицательным.", [nameof(CurrentNightReading)]);
        }

        if (OpeningDebtAmount.HasValue && OpeningDebtAmount.Value < 0m)
        {
            yield return new ValidationResult("Задолженность не может быть отрицательной.", [nameof(OpeningDebtAmount)]);
        }
    }
}
