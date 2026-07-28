using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.MemberTariffs;

public class TariffInputModel
{
    [Required(ErrorMessage = "Укажите дату начала действия.")]
    [DataType(DataType.Date)]
    public DateOnly? EffectiveFrom { get; set; }

    [Required(ErrorMessage = "Укажите тариф для участников.")]
    [Range(typeof(decimal), "0", "999999999999999.9999", ErrorMessage = "Тариф не может быть отрицательным.")]
    public decimal? Rate { get; set; }
}
