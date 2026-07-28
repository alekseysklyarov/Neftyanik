using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Ownerships;

public class OwnershipEndInputModel
{
    [Required(ErrorMessage = "Укажите дату аннулирования владения.")]
    [DataType(DataType.Date)]
    [Display(Name = "Дата аннулирования")]
    public DateOnly? ValidTo { get; set; }
}
