using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Identity;

public static class AssociationAccountAccess
{
    public static async Task<bool> CanManageGlobalAccountAsync(ApplicationDbContext database, string userId, CancellationToken cancellationToken)
    {
        // Passwords and profile fields are global. A tenant administrator must not take over
        // a platform account or an account used elsewhere, including an inactive membership or an existing member link.
        return database.IsAssociationResolved
            && !await database.UserRoles.AsNoTracking().Where(x => x.UserId == userId)
                .Join(database.Roles, assignment => assignment.RoleId, role => role.Id, (_, role) => role)
                .AnyAsync(role => role.NormalizedName == RoleNames.PlatformAdministrator.ToUpperInvariant(), cancellationToken)
            && await database.AssociationUserMemberships.AsNoTracking()
                .AnyAsync(x => x.ApplicationUserId == userId, cancellationToken)
            && !await database.AssociationUserMemberships.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.ApplicationUserId == userId && x.AssociationId != database.CurrentAssociationId, cancellationToken)
            && !await database.Members.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.ApplicationUserId == userId && x.AssociationId != database.CurrentAssociationId, cancellationToken);
    }
}
