using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Identity;

public static class AssociationAccountAccess
{
    public static async Task<bool> CanManageGlobalAccountAsync(ApplicationDbContext database, string userId, CancellationToken cancellationToken)
    {
        // Passwords and profile fields are global. A tenant administrator must not take over
        // an account used elsewhere, including an inactive membership or an existing member link.
        return database.IsAssociationResolved
            && await database.AssociationUserMemberships.AsNoTracking()
                .AnyAsync(x => x.ApplicationUserId == userId, cancellationToken)
            && !await database.AssociationUserMemberships.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.ApplicationUserId == userId && x.AssociationId != database.CurrentAssociationId, cancellationToken)
            && !await database.Members.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.ApplicationUserId == userId && x.AssociationId != database.CurrentAssociationId, cancellationToken);
    }
}
