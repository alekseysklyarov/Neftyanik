using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public abstract class PlotPageModelBase : PageModel
{
    protected PlotPageModelBase(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        DbContext = dbContext;
        UserManager = userManager;
    }

    protected ApplicationDbContext DbContext { get; }

    protected UserManager<ApplicationUser> UserManager { get; }

    protected async Task<PlotContextViewModel?> GetPlotContextAsync(int plotId, CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);

        return await DbContext.Plots
            .AsNoTracking()
            .Where(plot => plot.Id == plotId)
            .Select(plot => new PlotContextViewModel
            {
                PlotId = plot.Id,
                PlotNumber = plot.Number,
                PlotAddress = plot.Address,
                PlotIsActive = plot.IsActive,
                ActiveOwnersCount = plot.PlotOwnerships.Count(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                    && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate)),
                SpecifiedTotalShare = plot.PlotOwnerships
                    .Where(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                        && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate)
                        && ownership.OwnershipShare.HasValue)
                    .Sum(ownership => (decimal?)ownership.OwnershipShare) ?? 0m
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public sealed class PlotContextViewModel
    {
        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public string? PlotAddress { get; init; }

        public bool PlotIsActive { get; init; }

        public int ActiveOwnersCount { get; init; }

        public decimal SpecifiedTotalShare { get; init; }
    }
}
