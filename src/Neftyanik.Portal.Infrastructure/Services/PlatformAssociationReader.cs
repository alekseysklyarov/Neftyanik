using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class PlatformAssociationReader(
    ApplicationDbContext database,
    IPlatformAssociationReadAccess access,
    TimeProvider clock) : IPlatformAssociationReader
{
    private const int PageSize = 20;

    public async Task<PlatformAssociationHistoryPage> GetHistoryAsync(int associationId, int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureAllowedAsync(cancellationToken);
        var history = database.PlatformAuditLogs.AsNoTracking().Where(x => x.AssociationId == associationId);
        var totalCount = await history.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, totalCount / PageSize + (totalCount % PageSize == 0 ? 0 : 1));
        pageNumber = Math.Clamp(pageNumber, 1, totalPages);
        var rows = await history.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Skip((pageNumber - 1) * PageSize).Take(PageSize)
            .Select(x => new
            {
                x.Id, x.OccurredAtUtc, x.OperatorUserId,
                OperatorUserName = database.Users.Where(user => user.Id == x.OperatorUserId)
                    .Select(user => user.UserName).FirstOrDefault(),
                x.Action, x.OldValuesJson, x.NewValuesJson
            }).ToListAsync(cancellationToken);

        var items = rows.Select(x => new PlatformAssociationHistoryEntry(x.Id, x.OccurredAtUtc,
            x.OperatorUserId, x.OperatorUserName, x.Action,
            PlatformAssociationHistoryValues.Read(x.Action, x.OldValuesJson, x.NewValuesJson))).ToArray();
        return new(items, totalCount, pageNumber, totalPages);
    }

    public async Task<PlatformAssociationPage> GetPageAsync(string? search = null, bool? isActive = null,
        int pageNumber = 1, CancellationToken cancellationToken = default)
    {
        await access.EnsureAllowedAsync(cancellationToken);
        search = search?.Trim();
        if (search?.Length > 200)
        {
            throw new ArgumentException("Search must not exceed 200 characters.", nameof(search));
        }

        var associations = database.Associations.AsNoTracking();
        if (!string.IsNullOrEmpty(search))
        {
            associations = associations.Where(x => x.Name.Contains(search) || x.Slug.Contains(search));
        }
        if (isActive.HasValue)
        {
            associations = associations.Where(x => x.IsActive == isActive.Value);
        }

        var totalCount = await associations.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, totalCount / PageSize + (totalCount % PageSize == 0 ? 0 : 1));
        pageNumber = Math.Clamp(pageNumber, 1, totalPages);

        // Only metadata aggregates cross tenant boundaries, after platform authorization.
        var members = database.Members.IgnoreQueryFilters().AsNoTracking();
        var administrators = database.AssociationUserMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Role == RoleNames.Administrator && x.IsActive);
        var items = await associations.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((pageNumber - 1) * PageSize).Take(PageSize)
            .Select(x => new PlatformAssociationSummary(x.Id, x.Name, x.Slug, x.IsActive,
                members.Where(m => m.AssociationId == x.Id).Select(m => m.Id).Distinct().Count(),
                administrators.Where(m => m.AssociationId == x.Id)
                    .Select(m => m.ApplicationUserId).Distinct().Count()))
            .ToListAsync(cancellationToken);

        return new PlatformAssociationPage(items, totalCount, pageNumber, totalPages);
    }

    public async Task<PlatformAssociationDetails?> GetDetailsAsync(int associationId, CancellationToken cancellationToken = default)
    {
        await access.EnsureAllowedAsync(cancellationToken);
        var association = await database.Associations.AsNoTracking()
            .Where(x => x.Id == associationId)
            .Select(x => new { x.Id, x.Name, x.Slug, x.IsActive, x.CreatedAtUtc, x.ContactEmail, x.ContactPhone, x.PostalAddress })
            .SingleOrDefaultAsync(cancellationToken);
        if (association is null)
        {
            return null;
        }

        var memberCount = await database.Members.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.AssociationId == associationId).Select(x => x.Id).Distinct().CountAsync(cancellationToken);
        var assignments = await database.AssociationUserMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.AssociationId == associationId && x.Role == RoleNames.Administrator)
            .OrderBy(x => x.ApplicationUser.UserName).ThenBy(x => x.ApplicationUserId)
            .Select(x => new
            {
                x.ApplicationUser.UserName,
                IsAccountActive = x.ApplicationUser.IsActive,
                x.ApplicationUser.LockoutEnabled,
                x.ApplicationUser.LockoutEnd,
                IsAssignmentActive = x.IsActive
            }).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var administrators = assignments.Select(x => new PlatformAssociationAdministrator(
            x.UserName, x.IsAccountActive, x.LockoutEnabled && x.LockoutEnd > now, x.IsAssignmentActive)).ToArray();

        return new PlatformAssociationDetails(association.Id, association.Name, association.Slug,
            association.IsActive, association.CreatedAtUtc, memberCount, administrators,
            association.ContactEmail, association.ContactPhone, association.PostalAddress);
    }
}
