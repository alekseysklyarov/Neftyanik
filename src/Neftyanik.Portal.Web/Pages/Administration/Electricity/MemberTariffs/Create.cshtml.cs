using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;

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
        NormalizeRateInput();

        if (Input.Rate.HasValue && Input.Rate.Value < 0m)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(TariffInputModel.Rate)}", "Тариф не может быть отрицательным.");
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
                currentUser?.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Не удалось сохранить тариф для участников.");
            return Page();
        }

        TempData["SuccessMessage"] = "Тариф для участников сохранён.";
        return RedirectToPage("/Administration/Electricity/MemberTariffs/Index");
    }

    private void NormalizeRateInput()
    {
        var key = $"{nameof(Input)}.{nameof(TariffInputModel.Rate)}";
        if (Input.Rate.HasValue || !Request.HasFormContentType)
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

        Input.Rate = rate;
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
