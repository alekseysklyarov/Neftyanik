using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Ownerships;

public class OwnershipInputModel
{
    [Required(ErrorMessage = "Выберите члена товарищества.")]
    [Display(Name = "Член товарищества")]
    public int? MemberId { get; set; }

    [Range(typeof(decimal), "0.01", "100", ErrorMessage = "Доля владения должна быть больше 0 и не больше 100.")]
    [Display(Name = "Доля владения, %")]
    public decimal? OwnershipShare { get; set; }

    [Display(Name = "Основной контакт")]
    public bool IsPrimaryContact { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Действует с")]
    public DateOnly? ValidFrom { get; set; }
}
