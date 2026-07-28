using System.ComponentModel.DataAnnotations;
using Neftyanik.Portal.Domain.Enums;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

public class MemberPaymentInputModel : IValidatableObject
{
    [Display(Name = "Участок")]
    public int? PlotId { get; set; }

    [Required(ErrorMessage = "Укажите дату платежа.")]
    [DataType(DataType.Date)]
    [Display(Name = "Дата платежа")]
    public DateOnly? PaymentDate { get; set; }

    [Required(ErrorMessage = "Укажите сумму платежа.")]
    [Display(Name = "Сумма, грн")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Выберите способ оплаты.")]
    [Display(Name = "Способ оплаты")]
    public PaymentMethod? PaymentMethod { get; set; }

    [StringLength(200)]
    [Display(Name = "Номер документа / квитанции")]
    public string? ReferenceNumber { get; set; }

    [StringLength(1000)]
    [Display(Name = "Описание")]
    public string? Description { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount.HasValue && Amount.Value <= 0m)
        {
            yield return new ValidationResult("Сумма платежа должна быть больше нуля.", [nameof(Amount)]);
        }
    }
}
