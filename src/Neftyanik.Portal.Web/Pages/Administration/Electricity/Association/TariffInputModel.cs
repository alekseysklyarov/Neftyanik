using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Association;

public class TariffInputModel
{
    [Required(ErrorMessage = "Укажите дату начала действия.")]
    [DataType(DataType.Date)]
    public DateOnly? EffectiveFrom { get; set; }

    [Required(ErrorMessage = "Укажите дневной тариф поставщика.")]
    [Range(typeof(decimal), "0", "999999999999999.9999", ErrorMessage = "Тариф не может быть отрицательным.")]
    public decimal? DayRate { get; set; }

    [Required(ErrorMessage = "Укажите ночной тариф поставщика.")]
    [Range(typeof(decimal), "0", "999999999999999.9999", ErrorMessage = "Тариф не может быть отрицательным.")]
    public decimal? NightRate { get; set; }
}
