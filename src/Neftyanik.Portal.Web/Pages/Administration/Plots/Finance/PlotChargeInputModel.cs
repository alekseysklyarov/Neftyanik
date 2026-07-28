namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;

public class PlotChargeInputModel : System.ComponentModel.DataAnnotations.IValidatableObject
{
    [System.ComponentModel.DataAnnotations.Display(Name = "Выбранные участки")]
    public List<int> SelectedPlotIds { get; set; } = [];

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Выберите тип начисления.")]
    [System.ComponentModel.DataAnnotations.Display(Name = "Тип начисления")]
    public int? ChargeTypeId { get; set; }

    [System.ComponentModel.DataAnnotations.Display(Name = "Сумма, грн")]
    public decimal? Amount { get; set; }

    [System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Date)]
    [System.ComponentModel.DataAnnotations.Display(Name = "Дата начисления")]
    public DateOnly? ChargeDate { get; set; }

    [System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Date)]
    [System.ComponentModel.DataAnnotations.Display(Name = "Срок оплаты")]
    public DateOnly? DueDate { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(1000)]
    [System.ComponentModel.DataAnnotations.Display(Name = "Описание")]
    public string? Description { get; set; }

    public IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> Validate(System.ComponentModel.DataAnnotations.ValidationContext validationContext)
    {
        if (SelectedPlotIds.Count == 0)
        {
            yield return new System.ComponentModel.DataAnnotations.ValidationResult("Выберите хотя бы один участок.", [nameof(SelectedPlotIds)]);
        }

        if (Amount.HasValue && Amount.Value <= 0m)
        {
            yield return new System.ComponentModel.DataAnnotations.ValidationResult("Сумма начисления должна быть больше нуля.", [nameof(Amount)]);
        }

        if (DueDate.HasValue && ChargeDate.HasValue && DueDate.Value < ChargeDate.Value)
        {
            yield return new System.ComponentModel.DataAnnotations.ValidationResult("Срок оплаты не может быть раньше даты начисления.", [nameof(DueDate), nameof(ChargeDate)]);
        }
    }
}
