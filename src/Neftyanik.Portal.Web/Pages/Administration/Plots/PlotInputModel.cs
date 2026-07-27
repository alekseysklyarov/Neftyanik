using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots;

public class PlotInputModel
{
    [Required(ErrorMessage = "Укажите номер участка.")]
    [StringLength(50, ErrorMessage = "Номер участка не должен превышать 50 символов.")]
    [Display(Name = "Номер участка")]
    public string Number { get; set; } = string.Empty;

    [StringLength(250, ErrorMessage = "Адрес не должен превышать 250 символов.")]
    [Display(Name = "Адрес")]
    public string? Address { get; set; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Площадь не может быть отрицательной.")]
    [Display(Name = "Площадь, м²")]
    public decimal? AreaSquareMeters { get; set; }

    [StringLength(100, ErrorMessage = "Кадастровый номер не должен превышать 100 символов.")]
    [Display(Name = "Кадастровый номер")]
    public string? CadastralNumber { get; set; }

    [StringLength(2000, ErrorMessage = "Примечание не должно превышать 2000 символов.")]
    [Display(Name = "Примечание")]
    public string? Notes { get; set; }

    [Display(Name = "Активен")]
    public bool IsActive { get; set; } = true;
}
