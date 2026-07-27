using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Queries;

public static class MemberOwnershipQueries
{
    public static IQueryable<PlotOwnership> WhereCurrentOn(this IQueryable<PlotOwnership> query, DateOnly currentDate)
    {
        return query.Where(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
            && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate));
    }

    public static IQueryable<PlotOwnership> WhereCurrentForUser(this IQueryable<PlotOwnership> query, string applicationUserId, DateOnly currentDate)
    {
        return query.Where(ownership => ownership.Member != null
            && ownership.Member.ApplicationUserId == applicationUserId)
            .WhereCurrentOn(currentDate);
    }

    public static IQueryable<PlotOwnership> WhereCurrentForMember(this IQueryable<PlotOwnership> query, int memberId, DateOnly currentDate)
    {
        return query.Where(ownership => ownership.MemberId == memberId)
            .WhereCurrentOn(currentDate);
    }
}
