using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class PlatformOverviewReader(ApplicationDbContext database, IPlatformAssociationReadAccess access,
    TimeProvider clock) : IPlatformOverviewReader
{
    public async Task<PlatformOverview> GetAsync(CancellationToken cancellationToken = default)
    {
        await access.EnsureAllowedAsync(cancellationToken);
        var administrators = AvailableAdministrators();
        var associations = database.Associations.AsNoTracking();
        var total = await associations.CountAsync(cancellationToken);
        var active = associations.Where(x => x.IsActive);
        var activeCount = await active.CountAsync(cancellationToken);
        var without = await active.CountAsync(x => !administrators.Any(a => a.AssociationId == x.Id), cancellationToken);
        var awaiting = await active.CountAsync(x => administrators.Any(a => a.AssociationId == x.Id)
            && !administrators.Any(a => a.AssociationId == x.Id && !a.ApplicationUser.MustChangePassword), cancellationToken);
        var attention = await active.Where(x => !administrators.Any(a => a.AssociationId == x.Id && !a.ApplicationUser.MustChangePassword))
            .OrderBy(x => administrators.Any(a => a.AssociationId == x.Id)).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Take(10).Select(x => new PlatformAttentionItem(x.Id, x.Name, x.Slug,
                administrators.Any(a => a.AssociationId == x.Id))).ToListAsync(cancellationToken);
        return new(total, activeCount, without, awaiting, attention);
    }

    public async Task<PlatformAssociationSetup?> GetSetupAsync(int associationId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAllowedAsync(cancellationToken);
        var association = await database.Associations.AsNoTracking().Where(x => x.Id == associationId)
            .Select(x => new { x.ContactEmail, x.ContactPhone }).SingleOrDefaultAsync(cancellationToken);
        if (association is null) return null;
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var administrators = AvailableAdministrators().Where(x => x.AssociationId == associationId);
        var available = await administrators.CountAsync(cancellationToken);
        var onboarded = await administrators.CountAsync(x => !x.ApplicationUser.MustChangePassword, cancellationToken);
        var members = await database.Members.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.AssociationId == associationId && x.IsActive, cancellationToken);
        var plots = await database.Plots.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.AssociationId == associationId, cancellationToken);
        var ownerships = await database.PlotOwnerships.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.AssociationId == associationId && (!x.ValidFrom.HasValue || x.ValidFrom <= today)
                && (!x.ValidTo.HasValue || x.ValidTo >= today), cancellationToken);
        var types = await database.ChargeTypes.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.AssociationId == associationId && x.IsActive, cancellationToken);
        var memberTariff = await database.MemberElectricityTariffs.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.AssociationId == associationId && x.EffectiveFrom <= today, cancellationToken);
        var supplierTariff = await database.AssociationElectricityTariffs.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.AssociationId == associationId && x.EffectiveFrom <= today, cancellationToken);
        return new(!string.IsNullOrWhiteSpace(association.ContactEmail) || !string.IsNullOrWhiteSpace(association.ContactPhone),
            available, onboarded, members, plots, ownerships, types, memberTariff, supplierTariff);
    }

    private IQueryable<Neftyanik.Portal.Domain.Entities.AssociationUserMembership> AvailableAdministrators()
    {
        var now = clock.GetUtcNow();
        return database.AssociationUserMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Role == RoleNames.Administrator && x.IsActive && x.ApplicationUser.IsActive
                && (!x.ApplicationUser.LockoutEnabled || x.ApplicationUser.LockoutEnd == null || x.ApplicationUser.LockoutEnd <= now));
    }
}
