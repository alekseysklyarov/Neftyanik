using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters;

public class ReadingInputModel
{
    [Required(ErrorMessage = "Укажите дату показаний.")]
    [DataType(DataType.Date)]
    public DateOnly? ReadingDate { get; set; }

    [Required(ErrorMessage = "Укажите текущее показание.")]
    [Range(typeof(decimal), "0", "999999999999999.999", ErrorMessage = "Показание не может быть отрицательным.")]
    public decimal? CurrentReading { get; set; }
}
