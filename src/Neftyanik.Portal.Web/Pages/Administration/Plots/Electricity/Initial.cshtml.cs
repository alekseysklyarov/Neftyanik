using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Electricity;

public class InitialModel : PlotFinancePageModelBase
{
    private readonly IElectricityAccountingService _electricityAccountingService;

    public InitialModel(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IElectricityAccountingService electricityAccountingService)
        : base(dbContext, userManager)
    {
        _electricityAccountingService = electricityAccountingService;
    }

    [BindProperty]
    public InitialElectricityReadingInputModel Input { get; set; } = new();

    public PlotFinanceContextViewModel Plot { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int plotId, CancellationToken cancellationToken)
    {
        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        if (await HasHistoryAsync(plotId, cancellationToken))
        {
            TempData["ErrorMessage"] = "Начальные показания уже внесены.";
            return RedirectToPage("/Administration/Plots/Finance/Index", new { plotId });
        }

        Plot = plot;
        Input.ReadingDate = DateOnly.FromDateTime(DateTime.Today);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int plotId, CancellationToken cancellationToken)
    {
        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        Plot = plot;

        if (await HasHistoryAsync(plotId, cancellationToken))
        {
            ModelState.AddModelError(string.Empty, "Начальные показания уже внесены.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await UserManager.GetUserAsync(User);
        var result = await _electricityAccountingService.CreateInitialReadingAsync(
            new CreateInitialElectricityReadingRequest(
                plotId,
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

        TempData["SuccessMessage"] = "Начальные показания сохранены без создания начисления.";
        return RedirectToPage("/Administration/Plots/Finance/Index", new { plotId });
    }

    private async Task<bool> HasHistoryAsync(int plotId, CancellationToken cancellationToken)
    {
        return await DbContext.ElectricityReadings
            .AsNoTracking()
            .AnyAsync(reading => reading.PlotId == plotId, cancellationToken);
    }
}
