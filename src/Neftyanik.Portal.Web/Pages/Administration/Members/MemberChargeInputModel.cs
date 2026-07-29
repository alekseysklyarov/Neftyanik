using System.ComponentModel.DataAnnotations;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

public class MemberChargeInputModel : IValidatableObject
{
    public int? PlotId { get; set; }

    public int? ChargeTypeId { get; set; }

    public decimal? Amount { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? ChargeDate { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? DueDate { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount.HasValue && Amount.Value <= 0m)
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Сумма начисления должна быть больше нуля.", "Сума нарахування має бути більшою за нуль.", "The charge amount must be greater than zero."),
                [nameof(Amount)]);
        }

        if (DueDate.HasValue && ChargeDate.HasValue && DueDate.Value < ChargeDate.Value)
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Срок оплаты не может быть раньше даты начисления.", "Строк оплати не може бути раніше дати нарахування.", "The due date cannot be earlier than the charge date."),
                [nameof(DueDate), nameof(ChargeDate)]);
        }
    }
}
