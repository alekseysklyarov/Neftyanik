using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ExpenseCategories;

public class ExpenseCategoryInputModel
{
    [Required(ErrorMessage = "Укажите наименование типа расхода.")]
    [StringLength(200, ErrorMessage = "Наименование не должно превышать 200 символов.")]
    [Display(Name = "Наименование")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Описание не должно превышать 1000 символов.")]
    [Display(Name = "Описание")]
    public string? Description { get; set; }

    [Display(Name = "Активен")]
    public bool IsActive { get; set; } = true;
}
