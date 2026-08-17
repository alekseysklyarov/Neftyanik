namespace Neftyanik.Portal.Application.Identity;

public interface IUserActivityService
{
    Task RecordSuccessfulLoginAsync(RecordSuccessfulLoginRequest request, CancellationToken cancellationToken = default);

    Task<UserActivityDashboardSummary> GetDashboardSummaryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserActivityListItem>> GetUserActivityAsync(CancellationToken cancellationToken = default);
}

public sealed record RecordSuccessfulLoginRequest(
    string UserId,
    string? IpAddress,
    string? UserAgent);

public sealed record UserActivityDashboardSummary(
    int TotalRegisteredUsers,
    int EverLoggedInUsers,
    int ActiveTodayUsers,
    int ActiveLast7DaysUsers,
    int ActiveLast30DaysUsers)
{
    public int NeverLoggedInUsers => Math.Max(0, TotalRegisteredUsers - EverLoggedInUsers);
}

public sealed record UserActivityListItem(
    string UserId,
    string DisplayName,
    string? Login,
    string? Email,
    string? MemberNames,
    string? PlotNumbers,
    int TotalSuccessfulLogins,
    DateTimeOffset? LastLoginAtLocal,
    UserActivityStatus ActivityStatus);

public enum UserActivityStatus
{
    NeverLoggedIn = 0,
    Today = 1,
    Last7Days = 2,
    Last30Days = 3,
    MoreThan30DaysAgo = 4
}
