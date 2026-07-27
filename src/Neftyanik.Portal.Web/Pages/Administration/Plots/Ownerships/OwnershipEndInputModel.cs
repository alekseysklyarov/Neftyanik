using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Ownerships;

public class OwnershipEndInputModel
{
    [Required(ErrorMessage = "Укажите дату завершения владения.")]
    [DataType(DataType.Date)]
    [Display(Name = "Дата завершения")]
    public DateOnly? ValidTo { get; set; }
}
