using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Association;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class InitialModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAssociationElectricityService _associationElectricityService;
    private readonly UserManager<ApplicationUser> _userManager;

    public InitialModel(ApplicationDbContext dbContext, IAssociationElectricityService associationElectricityService, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _associationElectricityService = associationElectricityService;
        _userManager = userManager;
    }

    [BindProperty]
    public ReadingInputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await _dbContext.AssociationElectricityReadings.AsNoTracking().AnyAsync(cancellationToken))
        {
            TempData["ErrorMessage"] = "Начальные показания общего счётчика уже внесены.";
            return RedirectToPage("/Administration/Electricity/Association/Index");
        }

        Input.ReadingDate = DateOnly.FromDateTime(DateTime.Today);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (await _dbContext.AssociationElectricityReadings.AsNoTracking().AnyAsync(cancellationToken))
        {
            ModelState.AddModelError(string.Empty, "Начальные показания общего счётчика уже внесены.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var result = await _associationElectricityService.CreateInitialReadingAsync(
            new CreateAssociationElectricityInitialReadingRequest(
                Input.ReadingDate!.Value,
                Input.CurrentDayReading!.Value,
                Input.CurrentNightReading!.Value,
                currentUser?.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Не удалось сохранить начальные показания.");
            return Page();
        }

        TempData["SuccessMessage"] = "Начальные показания общего счётчика сохранены.";
        return RedirectToPage("/Administration/Electricity/Association/Index");
    }
}
