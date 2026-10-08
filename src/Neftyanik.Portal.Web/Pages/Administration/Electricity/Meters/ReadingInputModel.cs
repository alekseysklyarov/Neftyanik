using System.ComponentModel.DataAnnotations;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters;

public class ReadingInputModel
    : IValidatableObject
{
    [DataType(DataType.Date)]
    public DateOnly? ReadingDate { get; set; }

    public decimal? CurrentReading { get; set; }

    public decimal? CurrentNightReading { get; set; }

    public decimal? SecondMeterReading { get; set; }

    public decimal? SecondMeterNightReading { get; set; }

    public decimal? TotalReading => SumForPreview(CurrentReading, SecondMeterReading);

    public decimal? TotalNightReading => SumForPreview(CurrentNightReading, SecondMeterNightReading);

    private static decimal? SumForPreview(decimal? first, decimal? second)
    {
        try { return first + (second ?? 0m); }
        catch (OverflowException) { return null; }
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SecondMeterReading < 0m || SecondMeterNightReading < 0m)
            yield return new ValidationResult(AppLocalizer.Get("Показания второго счётчика не могут быть отрицательными.", "Показання другого лічильника не можуть бути від'ємними.", "The second meter readings cannot be negative."), [nameof(SecondMeterReading), nameof(SecondMeterNightReading)]);

        if (!ReadingDate.HasValue)
        {
            yield return new ValidationResult(AppLocalizer.Get("Укажите дату показаний.", "Вкажіть дату показань.", "Enter the reading date."), [nameof(ReadingDate)]);
        }

        if (!CurrentReading.HasValue)
        {
            yield return new ValidationResult(AppLocalizer.Get("Укажите текущее показание.", "Вкажіть поточний показник.", "Enter the current reading."), [nameof(CurrentReading)]);
        }

        if (CurrentReading.HasValue && CurrentReading.Value < 0m)
        {
            yield return new ValidationResult(AppLocalizer.Get("Показание не может быть отрицательным.", "Показник не може бути від'ємним.", "The reading cannot be negative."), [nameof(CurrentReading)]);
        }

        if (CurrentNightReading.HasValue && CurrentNightReading.Value < 0m)
        {
            yield return new ValidationResult(AppLocalizer.Get("Ночное показание не может быть отрицательным.", "Нічний показник не може бути від'ємним.", "The night reading cannot be negative."), [nameof(CurrentNightReading)]);
        }
    }
}
