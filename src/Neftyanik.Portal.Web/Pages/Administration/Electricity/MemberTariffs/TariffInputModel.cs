using System.ComponentModel.DataAnnotations;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.MemberTariffs;

public class TariffInputModel : IValidatableObject
{
    [DataType(DataType.Date)]
    public DateOnly? EffectiveFrom { get; set; }

    public decimal? Rate { get; set; }

    public decimal? NightRate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!EffectiveFrom.HasValue)
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Укажите дату начала действия.", "Вкажіть дату початку дії.", "Enter the effective start date."),
                [nameof(EffectiveFrom)]);
        }

        if (!Rate.HasValue)
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Укажите тариф для участников.", "Вкажіть тариф для учасників.", "Enter the member tariff."),
                [nameof(Rate)]);
        }
        else if (Rate.Value < 0m)
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Тариф не может быть отрицательным.", "Тариф не може бути від'ємним.", "The tariff cannot be negative."),
                [nameof(Rate)]);
        }

        if (NightRate.HasValue && NightRate.Value < 0m)
        {
            yield return new ValidationResult(
                AppLocalizer.Get("Ночной тариф не может быть отрицательным.", "Нічний тариф не може бути від'ємним.", "The night tariff cannot be negative."),
                [nameof(NightRate)]);
        }
    }
}
