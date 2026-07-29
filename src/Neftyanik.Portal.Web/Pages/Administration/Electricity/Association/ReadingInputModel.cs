using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Association;

public class ReadingInputModel
{
    [Required(ErrorMessage = "Укажите дату показаний.")]
    [DataType(DataType.Date)]
    public DateOnly? ReadingDate { get; set; }

    [Required(ErrorMessage = "Укажите дневное показание.")]
    [Range(typeof(decimal), "0", "999999999999999", ErrorMessage = "Показание не может быть отрицательным.")]
    public decimal? CurrentDayReading { get; set; }

    [Required(ErrorMessage = "Укажите ночное показание.")]
    [Range(typeof(decimal), "0", "999999999999999", ErrorMessage = "Показание не может быть отрицательным.")]
    public decimal? CurrentNightReading { get; set; }
}
