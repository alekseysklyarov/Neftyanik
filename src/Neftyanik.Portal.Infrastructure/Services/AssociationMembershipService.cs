using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class AssociationMembershipService(ApplicationDbContext dbContext) : IAssociationMembershipService
{
    public async Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!dbContext.IsAssociationResolved || string.IsNullOrWhiteSpace(userId))
        {
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null || !user.IsActive || (user.LockoutEnabled && user.LockoutEnd > now))
        {
            return [];
        }

        return await dbContext.AssociationUserMemberships.AsNoTracking()
            .Where(x => x.AssociationId == dbContext.CurrentAssociationId
                && x.ApplicationUserId == userId && x.IsActive && x.Association.IsActive
                && (x.Role == RoleNames.Administrator || x.Role == RoleNames.Accountant || x.Role == RoleNames.Member))
            .Select(x => x.Role).ToListAsync(cancellationToken);
    }
}
