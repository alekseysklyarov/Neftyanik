using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;

public class CancelPaymentModel : PlotFinancePageModelBase
{
    public CancelPaymentModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
        : base(dbContext, userManager)
    {
    }

    [BindProperty]
    public CancellationInputModel Input { get; set; } = new();

    public PlotFinanceContextViewModel Plot { get; private set; } = new();

    public PaymentContextViewModel Payment { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int plotId, long id, CancellationToken cancellationToken)
    {
        return await LoadPageAsync(plotId, id, cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(int plotId, long id, CancellationToken cancellationToken)
    {
        var payment = await DbContext.Payments.FirstOrDefaultAsync(item => item.Id == id && item.PlotId == plotId, cancellationToken);
        if (payment is null)
        {
            return NotFound();
        }

        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        Plot = plot;
        Payment = new PaymentContextViewModel
        {
            Id = payment.Id,
            Amount = payment.Amount,
            PaymentDate = payment.PaymentDate,
            PaymentMethodText = GetPaymentMethodText(payment.PaymentMethod),
            IsCancelled = payment.CancelledAtUtc != null
        };

        if (payment.CancelledAtUtc != null)
        {
            ModelState.AddModelError(string.Empty, "Этот платеж уже отменен.");
            return Page();
        }

        payment.CancelledAtUtc = DateTime.UtcNow;
        payment.CancellationReason = Normalize(Input.CancellationReason);
        await DbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = "Платеж отменен.";
        return RedirectToPage("/Administration/Plots/Finance/Index", new { plotId });
    }

    private async Task<IActionResult> LoadPageAsync(int plotId, long id, CancellationToken cancellationToken)
    {
        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        var payment = await DbContext.Payments
            .AsNoTracking()
            .Where(item => item.Id == id && item.PlotId == plotId)
            .Select(item => new PaymentContextViewModel
            {
                Id = item.Id,
                Amount = item.Amount,
                PaymentDate = item.PaymentDate,
                PaymentMethodText = GetPaymentMethodText(item.PaymentMethod),
                IsCancelled = item.CancelledAtUtc != null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            return NotFound();
        }

        Plot = plot;
        Payment = payment;
        return Page();
    }

    public sealed class PaymentContextViewModel
    {
        public long Id { get; init; }

        public decimal Amount { get; init; }

        public DateOnly PaymentDate { get; init; }

        public string PaymentMethodText { get; init; } = string.Empty;

        public bool IsCancelled { get; init; }
    }
}
