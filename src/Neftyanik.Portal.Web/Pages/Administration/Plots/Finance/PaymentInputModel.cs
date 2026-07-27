using System.ComponentModel.DataAnnotations;
using Neftyanik.Portal.Domain.Enums;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;

public class PaymentInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Укажите сумму платежа.")]
    [Display(Name = "Сумма")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Укажите дату платежа.")]
    [DataType(DataType.Date)]
    [Display(Name = "Дата платежа")]
    public DateOnly? PaymentDate { get; set; }

    [Required(ErrorMessage = "Выберите способ оплаты.")]
    [Display(Name = "Способ оплаты")]
    public PaymentMethod? PaymentMethod { get; set; }

    [StringLength(150, ErrorMessage = "Номер документа не должен превышать 150 символов.")]
    [Display(Name = "Номер документа")]
    public string? ReferenceNumber { get; set; }

    [StringLength(1000, ErrorMessage = "Описание не должно превышать 1000 символов.")]
    [Display(Name = "Описание")]
    public string? Description { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Amount.HasValue || Amount.Value <= 0m)
        {
            yield return new ValidationResult("Сумма платежа должна быть больше нуля.", [nameof(Amount)]);
        }
    }
}
