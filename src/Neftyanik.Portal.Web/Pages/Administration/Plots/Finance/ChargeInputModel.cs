using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;

public class ChargeInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Выберите тип начисления.")]
    [Display(Name = "Тип начисления")]
    public int? ChargeTypeId { get; set; }

    [Required(ErrorMessage = "Укажите сумму начисления.")]
    [Display(Name = "Сумма")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Укажите дату начисления.")]
    [DataType(DataType.Date)]
    [Display(Name = "Дата начисления")]
    public DateOnly? ChargeDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Срок оплаты")]
    public DateOnly? DueDate { get; set; }

    [Display(Name = "Год периода")]
    public int? PeriodYear { get; set; }

    [Display(Name = "Месяц периода")]
    public int? PeriodMonth { get; set; }

    [StringLength(1000, ErrorMessage = "Описание не должно превышать 1000 символов.")]
    [Display(Name = "Описание")]
    public string? Description { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Amount.HasValue || Amount.Value <= 0m)
        {
            yield return new ValidationResult("Сумма начисления должна быть больше нуля.", [nameof(Amount)]);
        }

        if (ChargeDate.HasValue && DueDate.HasValue && DueDate.Value < ChargeDate.Value)
        {
            yield return new ValidationResult("Срок оплаты не может быть раньше даты начисления.", [nameof(DueDate)]);
        }

        if (PeriodMonth.HasValue && (PeriodMonth.Value < 1 || PeriodMonth.Value > 12))
        {
            yield return new ValidationResult("Месяц периода должен быть в диапазоне от 1 до 12.", [nameof(PeriodMonth)]);
        }

        if (PeriodYear.HasValue && (PeriodYear.Value < 2000 || PeriodYear.Value > 2100))
        {
            yield return new ValidationResult("Год периода должен быть в диапазоне от 2000 до 2100.", [nameof(PeriodYear)]);
        }
    }
}
