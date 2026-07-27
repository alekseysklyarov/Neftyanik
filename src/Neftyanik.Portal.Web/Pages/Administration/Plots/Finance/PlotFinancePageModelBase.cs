using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Web.Pages.Finance;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;

[Authorize(Roles = RoleNames.Administrator)]
public abstract class PlotFinancePageModelBase : PageModel
{
    protected PlotFinancePageModelBase(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        DbContext = dbContext;
        UserManager = userManager;
    }

    protected ApplicationDbContext DbContext { get; }

    protected UserManager<ApplicationUser> UserManager { get; }

    protected async Task<PlotFinanceContextViewModel?> GetPlotContextAsync(int plotId, CancellationToken cancellationToken)
    {
        return await DbContext.Plots
            .AsNoTracking()
            .Where(plot => plot.Id == plotId)
            .SelectFinanceSummary()
            .Select(plot => new PlotFinanceContextViewModel
            {
                PlotId = plot.PlotId,
                PlotNumber = plot.PlotNumber,
                PlotAddress = plot.PlotAddress,
                ActiveChargesTotal = plot.ActiveChargesTotal,
                ActivePaymentsTotal = plot.ActivePaymentsTotal
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    protected async Task<IReadOnlyList<ChargeTypeOptionViewModel>> GetActiveChargeTypesAsync(CancellationToken cancellationToken)
    {
        return await DbContext.ChargeTypes
            .AsNoTracking()
            .Where(chargeType => chargeType.IsActive)
            .OrderBy(chargeType => chargeType.Name)
            .Select(chargeType => new ChargeTypeOptionViewModel
            {
                Id = chargeType.Id,
                Name = chargeType.Name,
                DefaultAmount = chargeType.DefaultAmount
            })
            .ToListAsync(cancellationToken);
    }

    protected static IReadOnlyList<SelectListItem> GetPaymentMethodOptions()
    {
        return Enum.GetValues<PaymentMethod>()
            .Select(method => new SelectListItem
            {
                Value = method.ToString(),
                Text = GetPaymentMethodText(method)
            })
            .ToList();
    }

    protected static string GetPaymentMethodText(PaymentMethod method)
    {
        return FinanceDisplayHelper.GetPaymentMethodText(method);
    }

    protected static string GetBalanceStatusText(decimal balance)
    {
        return FinanceDisplayHelper.GetBalanceStatusText(balance);
    }

    protected static string GetBalanceStatusClass(decimal balance)
    {
        return FinanceDisplayHelper.GetBalanceStatusClass(balance);
    }

    protected static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public sealed class PlotFinanceContextViewModel
    {
        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public string? PlotAddress { get; init; }

        public decimal ActiveChargesTotal { get; init; }

        public decimal ActivePaymentsTotal { get; init; }

        public decimal Balance => ActiveChargesTotal - ActivePaymentsTotal;

        public string BalanceStatus => GetBalanceStatusText(Balance);

        public string BalanceStatusClass => GetBalanceStatusClass(Balance);
    }

    public sealed class ChargeTypeOptionViewModel
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public decimal? DefaultAmount { get; init; }
    }
}
