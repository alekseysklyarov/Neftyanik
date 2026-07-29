using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Electricity.Tariffs;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CreateModel : PageModel
{
    private readonly IElectricityAccountingService _electricityAccountingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateModel(IElectricityAccountingService electricityAccountingService, UserManager<ApplicationUser> userManager)
    {
        _electricityAccountingService = electricityAccountingService;
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
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var result = await _electricityAccountingService.CreateTariffAsync(
            new CreateElectricityTariffRequest(
                Input.EffectiveFrom!.Value,
                Input.DayRate!.Value,
                Input.NightRate!.Value,
                currentUser?.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Не удалось сохранить тариф.");
            return Page();
        }

        TempData["SuccessMessage"] = "Тариф электроэнергии сохранен.";
        return RedirectToPage("/Administration/Finance/Electricity/Tariffs/Index");
    }
}
