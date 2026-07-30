using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.MemberTariffs;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CreateModel : PageModel
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private readonly IMemberElectricityService _memberElectricityService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateModel(IMemberElectricityService memberElectricityService, UserManager<ApplicationUser> userManager)
    {
        _memberElectricityService = memberElectricityService;
        _userManager = userManager;
    }

    [BindProperty]
    public TariffInputModel Input { get; set; } = new();

    public void OnGet()
    {
        Input.EffectiveFrom = DateOnly.FromDateTime(DateTime.Today);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        NormalizeRateInputs();

        if (Input.Rate.HasValue && Input.Rate.Value < 0m)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(TariffInputModel.Rate)}", AppLocalizer.Get("Тариф не может быть отрицательным.", "Тариф не може бути від'ємним.", "The tariff cannot be negative."));
        }

        if (Input.NightRate.HasValue && Input.NightRate.Value < 0m)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(TariffInputModel.NightRate)}", AppLocalizer.Get("Ночной тариф не может быть отрицательным.", "Нічний тариф не може бути від'ємним.", "The night tariff cannot be negative."));
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var result = await _memberElectricityService.CreateTariffAsync(
            new CreateMemberElectricityTariffRequest(
                Input.EffectiveFrom!.Value,
                Input.Rate!.Value,
                Input.NightRate,
                currentUser?.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? AppLocalizer.Get("Не удалось сохранить тариф для участников.", "Не вдалося зберегти тариф для учасників.", "Failed to save the member tariff."));
            return Page();
        }

        TempData["SuccessMessage"] = AppLocalizer.Get("Тариф для участников сохранён.", "Тариф для учасників збережено.", "The member tariff has been saved.");
        return RedirectToPage("/Administration/Electricity/MemberTariffs/Index");
    }

    private void NormalizeRateInputs()
    {
        NormalizeRateInput(nameof(TariffInputModel.Rate), Input.Rate, value => Input.Rate = value);
        NormalizeRateInput(nameof(TariffInputModel.NightRate), Input.NightRate, value => Input.NightRate = value);
    }

    private void NormalizeRateInput(string propertyName, decimal? currentValue, Action<decimal> assignValue)
    {
        var key = $"{nameof(Input)}.{propertyName}";
        if (currentValue.HasValue || !Request.HasFormContentType)
        {
            return;
        }

        var rawValue = Request.Form[key].ToString();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return;
        }

        var normalizedValue = rawValue.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!TryParseRate(normalizedValue, out var rate))
        {
            return;
        }

        assignValue(rate);
        ModelState.Remove(key);
    }

    private static bool TryParseRate(string value, out decimal rate)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out rate)
            || decimal.TryParse(value, NumberStyles.Number, RussianCulture, out rate)
            || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out rate)
            || decimal.TryParse(value.Replace('.', ','), NumberStyles.Number, RussianCulture, out rate)
            || decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out rate);
    }
}
