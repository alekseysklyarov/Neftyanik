using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

public class MemberChargeInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Выберите участок.")]
    [Display(Name = "Участок")]
    public int? PlotId { get; set; }

    [Required(ErrorMessage = "Выберите тип начисления.")]
    [Display(Name = "Тип начисления")]
    public int? ChargeTypeId { get; set; }

    [Required(ErrorMessage = "Укажите сумму начисления.")]
    [Display(Name = "Сумма, грн")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Укажите дату начисления.")]
    [DataType(DataType.Date)]
    [Display(Name = "Дата начисления")]
    public DateOnly? ChargeDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Срок оплаты")]
    public DateOnly? DueDate { get; set; }

    [StringLength(1000)]
    [Display(Name = "Описание")]
    public string? Description { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount.HasValue && Amount.Value <= 0m)
        {
            yield return new ValidationResult("Сумма начисления должна быть больше нуля.", [nameof(Amount)]);
        }

        if (DueDate.HasValue && ChargeDate.HasValue && DueDate.Value < ChargeDate.Value)
        {
            yield return new ValidationResult("Срок оплаты не может быть раньше даты начисления.", [nameof(DueDate), nameof(ChargeDate)]);
        }
    }
}
