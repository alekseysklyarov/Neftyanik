using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;

public class RegisterPaymentModel : PlotFinancePageModelBase
{
    public RegisterPaymentModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
        : base(dbContext, userManager)
    {
    }

    [BindProperty]
    public PaymentInputModel Input { get; set; } = new();

    public PlotFinanceContextViewModel Plot { get; private set; } = new();

    public IReadOnlyList<SelectListItem> PaymentMethods { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int plotId, CancellationToken cancellationToken)
    {
        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        Plot = plot;
        PaymentMethods = GetPaymentMethodOptions();
        Input.PaymentDate = DateOnly.FromDateTime(DateTime.Now);
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
        PaymentMethods = GetPaymentMethodOptions();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await UserManager.GetUserAsync(User);
        var payment = new Payment
        {
            PlotId = plotId,
            Amount = Input.Amount!.Value,
            PaymentDate = Input.PaymentDate!.Value,
            PaymentMethod = Input.PaymentMethod!.Value,
            ReferenceNumber = Normalize(Input.ReferenceNumber),
            Description = Normalize(Input.Description),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = currentUser?.Id
        };

        DbContext.Payments.Add(payment);
        await DbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = "Платеж успешно зарегистрирован.";
        return RedirectToPage("/Administration/Plots/Finance/Index", new { plotId });
    }
}
