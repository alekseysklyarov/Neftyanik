using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses;

public class ExpenseInputModel
{
    [Required(ErrorMessage = "Выберите тип расхода.")]
    [Display(Name = "Тип расхода")]
    public int? ExpenseCategoryId { get; set; }

    [Required(ErrorMessage = "Укажите дату расхода.")]
    [DataType(DataType.Date)]
    [Display(Name = "Дата расхода")]
    public DateOnly? ExpenseDate { get; set; }

    [Required(ErrorMessage = "Укажите сумму расхода.")]
    [Range(typeof(decimal), "0", "9999999999999999", ErrorMessage = "Сумма расхода не может быть отрицательной.")]
    [Display(Name = "Сумма")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Введите описание расхода.")]
    [StringLength(1000, ErrorMessage = "Описание не должно превышать 1000 символов.")]
    [Display(Name = "Описание")]
    public string Description { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Получатель не должен превышать 200 символов.")]
    [Display(Name = "Получатель")]
    public string? Payee { get; set; }

    [StringLength(100, ErrorMessage = "Номер документа не должен превышать 100 символов.")]
    [Display(Name = "Номер документа")]
    public string? DocumentNumber { get; set; }
}
