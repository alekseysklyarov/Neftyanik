using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class UserActivityServiceTests
{
    [Fact]
    public async Task GetDashboardSummaryAsync_ReturnsCountsAcrossActivityWindows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var fixedUtcNow = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Users.AddRange(
            CreateUser("user-1", "user1@example.com"),
            CreateUser("user-2", "user2@example.com"),
            CreateUser("user-3", "user3@example.com"),
            CreateUser("user-4", "user4@example.com"),
            CreateUser("user-5", "user5@example.com"));

        dbContext.UserLoginHistories.AddRange(
            new UserLoginHistory { UserId = "user-1", LoggedInAtUtc = fixedUtcNow.AddHours(-2) },
            new UserLoginHistory { UserId = "user-2", LoggedInAtUtc = fixedUtcNow.AddDays(-3) },
            new UserLoginHistory { UserId = "user-3", LoggedInAtUtc = fixedUtcNow.AddDays(-20) },
            new UserLoginHistory { UserId = "user-4", LoggedInAtUtc = fixedUtcNow.AddDays(-40) });

        await dbContext.SaveChangesAsync();

        var service = new UserActivityService(dbContext, new FixedTimeProvider(fixedUtcNow));

        var summary = await service.GetDashboardSummaryAsync();

        Assert.Equal(5, summary.TotalRegisteredUsers);
        Assert.Equal(4, summary.EverLoggedInUsers);
        Assert.Equal(1, summary.ActiveTodayUsers);
        Assert.Equal(2, summary.ActiveLast7DaysUsers);
        Assert.Equal(3, summary.ActiveLast30DaysUsers);
        Assert.Equal(1, summary.NeverLoggedInUsers);
    }

    [Fact]
    public async Task GetUserActivityAsync_OrdersUsersByLastLoginAndAggregatesPlots()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var fixedUtcNow = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Users.AddRange(
            CreateUser("user-1", "user1@example.com", displayName: "Петро Іваненко"),
            CreateUser("user-2", "user2@example.com", displayName: "Марія Савчук"),
            CreateUser("user-3", "user3@example.com", displayName: "Гість"));

        dbContext.Members.AddRange(
            new Member { Id = 1, FullName = "Петро Іваненко", ApplicationUserId = "user-1", IsActive = true },
            new Member { Id = 2, FullName = "Марія Савчук", ApplicationUserId = "user-2", IsActive = true });

        dbContext.Plots.AddRange(
            new Plot { Id = 10, Number = "P-10", IsActive = true },
            new Plot { Id = 20, Number = "P-20", IsActive = true });

        dbContext.PlotOwnerships.AddRange(
            new PlotOwnership { Id = 100, MemberId = 1, PlotId = 10, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true },
            new PlotOwnership { Id = 200, MemberId = 1, PlotId = 20, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true },
            new PlotOwnership { Id = 300, MemberId = 2, PlotId = 20, ValidFrom = new DateOnly(2020, 1, 1), ValidTo = new DateOnly(2020, 12, 31), IsPrimaryContact = true });

        dbContext.UserLoginHistories.AddRange(
            new UserLoginHistory { UserId = "user-1", LoggedInAtUtc = fixedUtcNow.AddDays(-1) },
            new UserLoginHistory { UserId = "user-1", LoggedInAtUtc = fixedUtcNow.AddHours(-3) },
            new UserLoginHistory { UserId = "user-2", LoggedInAtUtc = fixedUtcNow.AddDays(-15) });

        await dbContext.SaveChangesAsync();

        var service = new UserActivityService(dbContext, new FixedTimeProvider(fixedUtcNow));

        var users = await service.GetUserActivityAsync();

        Assert.Equal(3, users.Count);
        Assert.Equal("user-1", users[0].UserId);
        Assert.Equal(2, users[0].TotalSuccessfulLogins);
        Assert.Equal("P-10, P-20", users[0].PlotNumbers);
        Assert.Equal(UserActivityStatus.Today, users[0].ActivityStatus);

        Assert.Equal("user-2", users[1].UserId);
        Assert.Equal(UserActivityStatus.Last30Days, users[1].ActivityStatus);

        Assert.Equal("user-3", users[2].UserId);
        Assert.Equal(0, users[2].TotalSuccessfulLogins);
        Assert.Null(users[2].LastLoginAtLocal);
        Assert.Equal(UserActivityStatus.NeverLoggedIn, users[2].ActivityStatus);
    }

    [Fact]
    public async Task RecordSuccessfulLoginAsync_CreatesHistoryRecordAndTrimsDiagnosticFields()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var fixedUtcNow = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Users.Add(CreateUser("user-1", "user1@example.com"));
        await dbContext.SaveChangesAsync();

        var service = new UserActivityService(dbContext, new FixedTimeProvider(fixedUtcNow));

        await service.RecordSuccessfulLoginAsync(new RecordSuccessfulLoginRequest(
            "user-1",
            new string('1', 100),
            new string('A', 700)));

        var history = await dbContext.UserLoginHistories.AsNoTracking().SingleAsync();

        Assert.Equal("user-1", history.UserId);
        Assert.Equal(fixedUtcNow, history.LoggedInAtUtc);
        Assert.Equal(UserLoginHistory.IpAddressMaxLength, history.IpAddress?.Length);
        Assert.Equal(UserLoginHistory.UserAgentMaxLength, history.UserAgent?.Length);
    }

    private static ApplicationUser CreateUser(string id, string userName, string? displayName = null)
    {
        return new ApplicationUser
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = userName,
            NormalizedEmail = userName.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            FirstName = displayName ?? "Test",
            LastName = "User",
            DisplayName = displayName,
            IsActive = true
        };
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
