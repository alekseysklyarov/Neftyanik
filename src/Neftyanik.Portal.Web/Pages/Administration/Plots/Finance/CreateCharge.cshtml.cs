using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;

public class CreateChargeModel : PlotFinancePageModelBase
{
    public CreateChargeModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
        : base(dbContext, userManager)
    {
    }

    [BindProperty]
    public ChargeInputModel Input { get; set; } = new();

    public PlotFinanceContextViewModel Plot { get; private set; } = new();

    public IReadOnlyList<ChargeTypeOptionViewModel> ChargeTypes { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int plotId, CancellationToken cancellationToken)
    {
        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        Plot = plot;
        ChargeTypes = await GetActiveChargeTypesAsync(cancellationToken);
        Input.ChargeDate = DateOnly.FromDateTime(DateTime.Now);
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
        ChargeTypes = await GetActiveChargeTypesAsync(cancellationToken);

        if (!Input.ChargeTypeId.HasValue || !ChargeTypes.Any(item => item.Id == Input.ChargeTypeId.Value))
        {
            ModelState.AddModelError("Input.ChargeTypeId", "Выберите активный тип начисления.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await UserManager.GetUserAsync(User);
        var charge = new Charge
        {
            PlotId = plotId,
            ChargeTypeId = Input.ChargeTypeId!.Value,
            Amount = Input.Amount!.Value,
            ChargeDate = Input.ChargeDate!.Value,
            DueDate = Input.DueDate,
            PeriodYear = Input.PeriodYear,
            PeriodMonth = Input.PeriodMonth,
            Description = Normalize(Input.Description),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = currentUser?.Id
        };

        DbContext.Charges.Add(charge);
        await DbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = "Начисление успешно добавлено.";
        return RedirectToPage("/Administration/Plots/Finance/Index", new { plotId });
    }
}
