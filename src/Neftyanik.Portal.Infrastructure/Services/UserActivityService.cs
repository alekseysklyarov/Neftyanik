using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;

namespace Neftyanik.Portal.Infrastructure.Services;

public class UserActivityService : IUserActivityService
{
    private static readonly TimeZoneInfo PortalTimeZone = ResolvePortalTimeZone();
    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public UserActivityService(ApplicationDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task RecordSuccessfulLoginAsync(RecordSuccessfulLoginRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);

        var history = new UserLoginHistory
        {
            UserId = request.UserId,
            LoggedInAtUtc = _timeProvider.GetUtcNow(),
            IpAddress = TrimToLength(request.IpAddress, UserLoginHistory.IpAddressMaxLength),
            UserAgent = TrimToLength(request.UserAgent, UserLoginHistory.UserAgentMaxLength)
        };

        _dbContext.UserLoginHistories.Add(history);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserActivityDashboardSummary> GetDashboardSummaryAsync(CancellationToken cancellationToken = default)
    {
        var boundaries = CreateBoundaries();
        var totalRegisteredUsers = await _dbContext.Users.AsNoTracking().CountAsync(cancellationToken);
        var loginSummaries = await GetLoginSummariesAsync(cancellationToken);

        var everLoggedInUsers = loginSummaries.Count;
        var activeTodayUsers = loginSummaries.Count(summary => summary.LastLoginAtUtc >= boundaries.TodayStartUtc);
        var activeLast7DaysUsers = loginSummaries.Count(summary => summary.LastLoginAtUtc >= boundaries.Last7DaysStartUtc);
        var activeLast30DaysUsers = loginSummaries.Count(summary => summary.LastLoginAtUtc >= boundaries.Last30DaysStartUtc);

        return new UserActivityDashboardSummary(
            totalRegisteredUsers,
            everLoggedInUsers,
            activeTodayUsers,
            activeLast7DaysUsers,
            activeLast30DaysUsers);
    }

    public async Task<IReadOnlyList<UserActivityListItem>> GetUserActivityAsync(CancellationToken cancellationToken = default)
    {
        var boundaries = CreateBoundaries();
        var users = await _dbContext.Users
            .AsNoTracking()
            .Select(user => new UserRecord(
                user.Id,
                user.UserName,
                user.Email,
                user.DisplayName,
                user.FirstName,
                user.LastName))
            .ToListAsync(cancellationToken);

        var loginSummaryByUserId = (await GetLoginSummariesAsync(cancellationToken))
            .ToDictionary(summary => summary.UserId, StringComparer.Ordinal);

        var memberNamesByUserId = (await _dbContext.Members
            .AsNoTracking()
            .Where(member => member.ApplicationUserId != null)
            .Select(member => new UserMemberRecord(member.ApplicationUserId!, member.FullName))
            .ToListAsync(cancellationToken))
            .GroupBy(record => record.UserId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => JoinDistinct(group.Select(item => item.MemberName)),
                StringComparer.Ordinal);

        var plotNumbersByUserId = (await (
            from ownership in _dbContext.PlotOwnerships.AsNoTracking().WhereCurrentOn(boundaries.CurrentLocalDate)
            join member in _dbContext.Members.AsNoTracking() on ownership.MemberId equals member.Id
            join plot in _dbContext.Plots.AsNoTracking() on ownership.PlotId equals plot.Id
            where member.ApplicationUserId != null
            select new UserPlotRecord(member.ApplicationUserId!, plot.Number))
            .ToListAsync(cancellationToken))
            .GroupBy(record => record.UserId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => JoinDistinct(group.Select(item => item.PlotNumber)),
                StringComparer.Ordinal);

        var items = users
            .Select(user =>
            {
                loginSummaryByUserId.TryGetValue(user.Id, out var loginSummary);
                memberNamesByUserId.TryGetValue(user.Id, out var memberNames);
                plotNumbersByUserId.TryGetValue(user.Id, out var plotNumbers);

                return new UserActivityListItem(
                    user.Id,
                    ResolveDisplayName(user, memberNames),
                    user.UserName,
                    user.Email,
                    memberNames,
                    plotNumbers,
                    loginSummary?.TotalSuccessfulLogins ?? 0,
                    ConvertToPortalTime(loginSummary?.LastLoginAtUtc),
                    GetActivityStatus(loginSummary?.LastLoginAtUtc, boundaries));
            })
            .OrderBy(item => item.LastLoginAtLocal is null ? 1 : 0)
            .ThenByDescending(item => item.LastLoginAtLocal)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCulture)
            .ToList();

        return items;
    }

    private async Task<List<UserLoginSummaryRecord>> GetLoginSummariesAsync(CancellationToken cancellationToken)
    {
        var loginEvents = await _dbContext.UserLoginHistories
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return loginEvents
            .GroupBy(history => history.UserId, StringComparer.Ordinal)
            .Select(group => new UserLoginSummaryRecord(
                group.Key,
                group.Count(),
                group.Max(history => history.LoggedInAtUtc)))
            .ToList();
    }

    private BoundaryContext CreateBoundaries()
    {
        var localNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), PortalTimeZone);
        var currentLocalDate = DateOnly.FromDateTime(localNow.DateTime);

        return new BoundaryContext(
            currentLocalDate,
            ConvertLocalDateToUtc(currentLocalDate),
            ConvertLocalDateToUtc(currentLocalDate.AddDays(-6)),
            ConvertLocalDateToUtc(currentLocalDate.AddDays(-29)));
    }

    private static UserActivityStatus GetActivityStatus(DateTimeOffset? lastLoginAtUtc, BoundaryContext boundaries)
    {
        if (!lastLoginAtUtc.HasValue)
        {
            return UserActivityStatus.NeverLoggedIn;
        }

        if (lastLoginAtUtc.Value >= boundaries.TodayStartUtc)
        {
            return UserActivityStatus.Today;
        }

        if (lastLoginAtUtc.Value >= boundaries.Last7DaysStartUtc)
        {
            return UserActivityStatus.Last7Days;
        }

        if (lastLoginAtUtc.Value >= boundaries.Last30DaysStartUtc)
        {
            return UserActivityStatus.Last30Days;
        }

        return UserActivityStatus.MoreThan30DaysAgo;
    }

    private static DateTimeOffset? ConvertToPortalTime(DateTimeOffset? value)
    {
        return value.HasValue
            ? TimeZoneInfo.ConvertTime(value.Value, PortalTimeZone)
            : null;
    }

    private static DateTimeOffset ConvertLocalDateToUtc(DateOnly localDate)
    {
        var localMidnight = DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(localMidnight, PortalTimeZone);
        return new DateTimeOffset(utcDateTime, TimeSpan.Zero);
    }

    private static string ResolveDisplayName(UserRecord user, string? memberNames)
    {
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            return user.DisplayName;
        }

        var firstAndLastName = string.Join(' ', new[] { user.FirstName, user.LastName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        if (!string.IsNullOrWhiteSpace(firstAndLastName))
        {
            return firstAndLastName;
        }

        if (!string.IsNullOrWhiteSpace(memberNames))
        {
            return memberNames;
        }

        return user.UserName
            ?? user.Email
            ?? user.Id;
    }

    private static string? JoinDistinct(IEnumerable<string?> values)
    {
        var items = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.CurrentCulture)
            .OrderBy(value => value, StringComparer.CurrentCulture)
            .ToArray();

        return items.Length == 0 ? null : string.Join(", ", items);
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength].TrimEnd();
    }

    private static TimeZoneInfo ResolvePortalTimeZone()
    {
        foreach (var timeZoneId in new[] { "Europe/Kyiv", "FLE Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    private sealed record BoundaryContext(
        DateOnly CurrentLocalDate,
        DateTimeOffset TodayStartUtc,
        DateTimeOffset Last7DaysStartUtc,
        DateTimeOffset Last30DaysStartUtc);

    private sealed record UserRecord(
        string Id,
        string? UserName,
        string? Email,
        string? DisplayName,
        string FirstName,
        string LastName);

    private sealed record UserMemberRecord(string UserId, string MemberName);

    private sealed record UserPlotRecord(string UserId, string PlotNumber);

    private sealed record UserLoginSummaryRecord(
        string UserId,
        int TotalSuccessfulLogins,
        DateTimeOffset LastLoginAtUtc);
}
