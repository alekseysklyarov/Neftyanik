using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Ownerships;

[Authorize(Roles = RoleNames.Administrator)]
public abstract class OwnershipPageModelBase : PageModel
{
    protected OwnershipPageModelBase(ApplicationDbContext dbContext)
    {
        DbContext = dbContext;
    }

    protected ApplicationDbContext DbContext { get; }

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
                HasActivePrimaryContact = plot.PlotOwnerships.Any(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                    && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate)
                    && ownership.IsPrimaryContact),
                PrimaryContact = plot.PlotOwnerships
                    .Where(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                        && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate)
                        && ownership.IsPrimaryContact
                        && ownership.Member != null)
                    .Select(ownership => ownership.Member!.FullName)
                    .FirstOrDefault(),
                SpecifiedTotalShare = plot.PlotOwnerships
                    .Where(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                        && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate)
                        && ownership.OwnershipShare.HasValue)
                    .Sum(ownership => (decimal?)ownership.OwnershipShare) ?? 0m
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    protected async Task<IReadOnlyList<SelectListItem>> GetMemberOptionsAsync(int plotId, int? selectedMemberId, CancellationToken cancellationToken)
    {
        var activeOwnershipMemberIds = await DbContext.PlotOwnerships
            .AsNoTracking()
            .Where(ownership => ownership.PlotId == plotId && ownership.ValidTo == null)
            .Select(ownership => ownership.MemberId)
            .ToListAsync(cancellationToken);

        var members = await DbContext.Members
            .AsNoTracking()
            .Where(member => member.IsActive || (selectedMemberId.HasValue && member.Id == selectedMemberId.Value))
            .OrderBy(member => member.FullName)
            .Select(member => new
            {
                member.Id,
                member.FullName,
                member.PhoneNumber,
                member.Email
            })
            .ToListAsync(cancellationToken);

        return members
            .Where(member => !activeOwnershipMemberIds.Contains(member.Id) || (selectedMemberId.HasValue && member.Id == selectedMemberId.Value))
            .Select(member => new SelectListItem
            {
                Value = member.Id.ToString(),
                Text = BuildMemberDisplayText(member.FullName, member.PhoneNumber, member.Email)
            })
            .ToList();
    }

    protected async Task<bool> HasDuplicateActiveOwnershipAsync(int plotId, int memberId, int? excludedOwnershipId, CancellationToken cancellationToken)
    {
        return await DbContext.PlotOwnerships
            .AsNoTracking()
            .AnyAsync(
                ownership => ownership.PlotId == plotId
                    && ownership.MemberId == memberId
                    && ownership.ValidTo == null
                    && (!excludedOwnershipId.HasValue || ownership.Id != excludedOwnershipId.Value),
                cancellationToken);
    }

    protected async Task<bool> IsActiveMemberAsync(int memberId, CancellationToken cancellationToken)
    {
        return await DbContext.Members
            .AsNoTracking()
            .AnyAsync(member => member.Id == memberId && member.IsActive, cancellationToken);
    }

    protected async Task<decimal> GetSpecifiedActiveOwnershipShareTotalAsync(int plotId, int? excludedOwnershipId, CancellationToken cancellationToken)
    {
        return await DbContext.PlotOwnerships
            .AsNoTracking()
            .Where(ownership => ownership.PlotId == plotId
                && ownership.ValidTo == null
                && ownership.OwnershipShare.HasValue
                && (!excludedOwnershipId.HasValue || ownership.Id != excludedOwnershipId.Value))
            .SumAsync(ownership => ownership.OwnershipShare ?? 0m, cancellationToken);
    }

    protected void ValidateTotalShare(decimal existingSpecifiedTotal, decimal? ownershipShare)
    {
        if (ownershipShare.HasValue && existingSpecifiedTotal + ownershipShare.Value > 100m)
        {
            ModelState.AddModelError(string.Empty, "Суммарная указанная доля активных владельцев не может превышать 100%.");
        }
    }

    protected void ValidateDateRange(DateOnly? validFrom, DateOnly? validTo)
    {
        if (validFrom.HasValue && validTo.HasValue && validTo.Value < validFrom.Value)
        {
            ModelState.AddModelError(string.Empty, "Дата окончания не может быть раньше даты начала владения.");
        }
    }

    protected async Task ClearOtherPrimaryContactsAsync(int plotId, int ownershipId, CancellationToken cancellationToken)
    {
        var otherActivePrimaryOwnerships = await DbContext.PlotOwnerships
            .Where(ownership => ownership.PlotId == plotId
                && ownership.Id != ownershipId
                && ownership.ValidTo == null
                && ownership.IsPrimaryContact)
            .ToListAsync(cancellationToken);

        foreach (var ownership in otherActivePrimaryOwnerships)
        {
            ownership.IsPrimaryContact = false;
        }
    }

    protected static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string BuildMemberDisplayText(string fullName, string? phoneNumber, string? email)
    {
        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            return $"{fullName} ({phoneNumber})";
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            return $"{fullName} ({email})";
        }

        return fullName;
    }

    public sealed class PlotContextViewModel
    {
        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public string? PlotAddress { get; init; }

        public bool PlotIsActive { get; init; }

        public int ActiveOwnersCount { get; init; }

        public bool HasActivePrimaryContact { get; init; }

        public string? PrimaryContact { get; init; }

        public decimal SpecifiedTotalShare { get; init; }
    }
}
