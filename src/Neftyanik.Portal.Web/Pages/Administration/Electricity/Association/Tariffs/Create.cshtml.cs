using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using System.Globalization;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Association.Tariffs;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CreateModel : PageModel
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private readonly IAssociationElectricityService _associationElectricityService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateModel(IAssociationElectricityService associationElectricityService, UserManager<ApplicationUser> userManager)
    {
        _associationElectricityService = associationElectricityService;
        _userManager = userManager;
    }

    [BindProperty]
    public Pages.Administration.Electricity.Association.TariffInputModel Input { get; set; } = new();

    public void OnGet()
    {
        Input.EffectiveFrom = DateOnly.FromDateTime(DateTime.Today);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        NormalizeRateInputs();

        if (Input.DayRate.HasValue && Input.DayRate.Value < 0m)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Pages.Administration.Electricity.Association.TariffInputModel.DayRate)}", "Дневной тариф поставщика не может быть отрицательным.");
        }

        if (Input.NightRate.HasValue && Input.NightRate.Value < 0m)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Pages.Administration.Electricity.Association.TariffInputModel.NightRate)}", "Ночной тариф поставщика не может быть отрицательным.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var result = await _associationElectricityService.CreateTariffAsync(
            new CreateAssociationElectricityTariffRequest(
                Input.EffectiveFrom!.Value,
                Input.DayRate!.Value,
                Input.NightRate!.Value,
                currentUser?.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Не удалось сохранить тариф поставщика.");
            return Page();
        }

        TempData["SuccessMessage"] = "Тариф поставщика сохранён.";
        return RedirectToPage("/Administration/Electricity/Association/Tariffs/Index");
    }

    private void NormalizeRateInputs()
    {
        NormalizeRateInput(nameof(Pages.Administration.Electricity.Association.TariffInputModel.DayRate), Input.DayRate, value => Input.DayRate = value);
        NormalizeRateInput(nameof(Pages.Administration.Electricity.Association.TariffInputModel.NightRate), Input.NightRate, value => Input.NightRate = value);
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
