using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Association.Tariffs;

[Authorize(Roles = RoleNames.Administrator)]
public class CreateModel : PageModel
{
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
}
