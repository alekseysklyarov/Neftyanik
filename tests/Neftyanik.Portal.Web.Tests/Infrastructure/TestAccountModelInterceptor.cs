using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

internal sealed class TestAccountModelInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not ApplicationDbContext database) return result;
        var bindings = await database.AssociationAccountBindings.AsNoTracking()
            .Select(x => new { x.ApplicationUserId, x.AssociationId }).ToListAsync(cancellationToken);
        var memberships = await database.AssociationUserMemberships.IgnoreQueryFilters().AsNoTracking()
            .Select(x => new { x.ApplicationUserId, x.AssociationId }).ToListAsync(cancellationToken);
        var members = await database.Members.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.ApplicationUserId != null)
            .Select(x => new { ApplicationUserId = x.ApplicationUserId!, x.AssociationId }).ToListAsync(cancellationToken);
        var links = memberships.Concat(members).Concat(bindings).Distinct().ToArray();
        Assert.All(links.GroupBy(x => x.ApplicationUserId), group => Assert.Single(group));
        Assert.All(links, link => Assert.Contains(link, bindings));
        var platformUsers = await database.UserRoles.AsNoTracking()
            .Join(database.Roles, assignment => assignment.RoleId, role => role.Id, (assignment, role) => new { assignment.UserId, role.NormalizedName })
            .Where(x => x.NormalizedName == RoleNames.PlatformAdministrator.ToUpperInvariant())
            .Select(x => x.UserId).ToListAsync(cancellationToken);
        Assert.DoesNotContain(bindings, binding => platformUsers.Contains(binding.ApplicationUserId));
        return result;
    }
}
