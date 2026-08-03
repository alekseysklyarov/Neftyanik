#if WEB_TESTS
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Payments;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public sealed class PaymentNotificationServiceTests
{
    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesPendingNotificationWithoutPayment()
    {
        await using var testContext = await PaymentNotificationTestContext.CreateAsync();
        await testContext.SeedMemberAsync(1, "member-user");

        var service = testContext.CreatePaymentNotificationService();
        var result = await service.CreateAsync(1, new CreatePaymentNotificationRequest(150m, PaymentMethod.Card, "  квитанция  "));

        Assert.True(result.Succeeded);

        var notification = await testContext.DbContext.PaymentNotifications.SingleAsync();
        Assert.Equal(PaymentNotificationStatus.Pending, notification.Status);
        Assert.Equal(150m, notification.Amount);
        Assert.Equal("квитанция", notification.Description);
        Assert.Null(notification.PaymentId);
        Assert.Empty(await testContext.DbContext.Payments.ToListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task CreateAsync_NonPositiveAmount_IsRejected(decimal amount)
    {
        await using var testContext = await PaymentNotificationTestContext.CreateAsync();
        await testContext.SeedMemberAsync(1, "member-user");

        var service = testContext.CreatePaymentNotificationService();
        var result = await service.CreateAsync(1, new CreatePaymentNotificationRequest(amount, PaymentMethod.Cash, null));

        Assert.Equal(PaymentNotificationOperationResultCode.InvalidRequest, result.Code);
        Assert.Empty(await testContext.DbContext.PaymentNotifications.ToListAsync());
    }

    [Fact]
    public async Task GetRecentForMemberAsync_ReturnsOnlySpecifiedMemberNotifications()
    {
        await using var testContext = await PaymentNotificationTestContext.CreateAsync();
        await testContext.SeedMemberAsync(1, "member-1");
        await testContext.SeedMemberAsync(2, "member-2");

        testContext.DbContext.PaymentNotifications.AddRange(
            new PaymentNotification
            {
                MemberId = 1,
                Amount = 100m,
                PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20)
            },
            new PaymentNotification
            {
                MemberId = 2,
                Amount = 200m,
                PaymentMethod = PaymentMethod.Card,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
            },
            new PaymentNotification
            {
                MemberId = 1,
                Amount = 300m,
                PaymentMethod = PaymentMethod.BankTransfer,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        await testContext.DbContext.SaveChangesAsync();

        var service = testContext.CreatePaymentNotificationService();
        var items = await service.GetRecentForMemberAsync(1, 10);

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(1, item.MemberId));
        Assert.Equal(300m, items[0].Amount);
        Assert.Equal(100m, items[1].Amount);
    }

    [Fact]
    public async Task GetForAdministrationAsync_PendingFilter_ReturnsOldestFirst()
    {
        await using var testContext = await PaymentNotificationTestContext.CreateAsync();
        await testContext.SeedMemberAsync(1, "member-1", "Member One");

        testContext.DbContext.PaymentNotifications.AddRange(
            new PaymentNotification
            {
                MemberId = 1,
                Amount = 120m,
                PaymentMethod = PaymentMethod.Cash,
                Status = PaymentNotificationStatus.Pending,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-2)
            },
            new PaymentNotification
            {
                MemberId = 1,
                Amount = 130m,
                PaymentMethod = PaymentMethod.Cash,
                Status = PaymentNotificationStatus.Pending,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
            },
            new PaymentNotification
            {
                MemberId = 1,
                Amount = 140m,
                PaymentMethod = PaymentMethod.Cash,
                Status = PaymentNotificationStatus.Confirmed,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        await testContext.DbContext.SaveChangesAsync();

        var service = testContext.CreatePaymentNotificationService();
        var items = await service.GetForAdministrationAsync(new GetPaymentNotificationsForAdministrationRequest(PaymentNotificationStatus.Pending, 10));

        Assert.Equal(2, items.Count);
        Assert.True(items[0].CreatedAtUtc <= items[1].CreatedAtUtc);
        Assert.All(items, item => Assert.Equal(PaymentNotificationStatus.Pending, item.Status));
    }

    [Fact]
    public async Task GetForAdministrationAsync_NoNotifications_ReturnsEmptyList()
    {
        await using var testContext = await PaymentNotificationTestContext.CreateAsync();

        var service = testContext.CreatePaymentNotificationService();
        var items = await service.GetForAdministrationAsync(new GetPaymentNotificationsForAdministrationRequest(PaymentNotificationStatus.Pending, 10));

        Assert.Empty(items);
    }

    [Fact]
    public async Task GetForAdministrationAsync_HandlesOptionalFieldsAndMemberWithoutPlot()
    {
        await using var testContext = await PaymentNotificationTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, plotId: 101, chargeAmount: 200m);
        await testContext.SeedMemberAsync(2, "member-without-plot", "Member Without Plot");

        testContext.DbContext.Users.Add(new ApplicationUser
        {
            Id = "admin-user",
            UserName = "admin@example.com",
            NormalizedUserName = "ADMIN@EXAMPLE.COM",
            Email = "admin@example.com",
            NormalizedEmail = "ADMIN@EXAMPLE.COM",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            FirstName = "Admin",
            LastName = "User",
            DisplayName = "Portal Admin"
        });

        testContext.DbContext.Payments.Add(new Payment
        {
            Id = 5001,
            PlotId = 101,
            PaymentDate = new DateOnly(2026, 8, 1),
            Amount = 50m,
            PaymentMethod = PaymentMethod.BankTransfer,
            CreatedByUserId = "admin-user",
            CreatedAtUtc = DateTime.UtcNow
        });

        testContext.DbContext.PaymentNotifications.AddRange(
            new PaymentNotification
            {
                Id = 1001,
                MemberId = 1,
                Amount = 10m,
                PaymentMethod = PaymentMethod.Cash,
                Status = PaymentNotificationStatus.Pending,
                Description = null,
                PaymentId = null,
                ReviewedAtUtc = null,
                ReviewedByUserId = null,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-2)
            },
            new PaymentNotification
            {
                Id = 1002,
                MemberId = 1,
                Amount = 20m,
                PaymentMethod = PaymentMethod.BankTransfer,
                Status = PaymentNotificationStatus.Confirmed,
                Description = "Confirmed item",
                PaymentId = 5001,
                ReviewedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
                ReviewedByUserId = "admin-user",
                CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
            },
            new PaymentNotification
            {
                Id = 1003,
                MemberId = 2,
                Amount = 30m,
                PaymentMethod = PaymentMethod.Card,
                Status = PaymentNotificationStatus.Rejected,
                Description = "Rejected item",
                AdministratorComment = "Reason",
                ReviewedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                ReviewedByUserId = "admin-user",
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30)
            });
        await testContext.DbContext.SaveChangesAsync();

        var service = testContext.CreatePaymentNotificationService();

        var pendingItems = await service.GetForAdministrationAsync(new GetPaymentNotificationsForAdministrationRequest(PaymentNotificationStatus.Pending, 10));
        var confirmedItems = await service.GetForAdministrationAsync(new GetPaymentNotificationsForAdministrationRequest(PaymentNotificationStatus.Confirmed, 10));
        var rejectedItems = await service.GetForAdministrationAsync(new GetPaymentNotificationsForAdministrationRequest(PaymentNotificationStatus.Rejected, 10));

        var pendingItem = Assert.Single(pendingItems);
        Assert.Null(pendingItem.Description);
        Assert.Null(pendingItem.PaymentId);
        Assert.Null(pendingItem.ReviewedAtUtc);
        Assert.Contains("P-101", pendingItem.MemberPlotNumbers);

        var confirmedItem = Assert.Single(confirmedItems);
        Assert.Equal(5001, confirmedItem.PaymentId);
        Assert.Equal("Portal Admin", confirmedItem.ReviewedByUserDisplayName);
        Assert.Contains("P-101", confirmedItem.MemberPlotNumbers);

        var rejectedItem = Assert.Single(rejectedItems);
        Assert.Equal("Reason", rejectedItem.AdministratorComment);
        Assert.Empty(rejectedItem.MemberPlotNumbers);
    }

    [Fact]
    public async Task GetPendingCountAsync_CountsOnlyPendingNotifications()
    {
        await using var testContext = await PaymentNotificationTestContext.CreateAsync();
        await testContext.SeedMemberAsync(1, "member-user");
        await testContext.SeedUserAsync("admin", "admin@example.com");

        testContext.DbContext.PaymentNotifications.AddRange(
            new PaymentNotification { MemberId = 1, Amount = 10m, PaymentMethod = PaymentMethod.Cash, Status = PaymentNotificationStatus.Pending },
            new PaymentNotification { MemberId = 1, Amount = 20m, PaymentMethod = PaymentMethod.Cash, Status = PaymentNotificationStatus.Pending },
            new PaymentNotification { MemberId = 1, Amount = 30m, PaymentMethod = PaymentMethod.Cash, Status = PaymentNotificationStatus.Confirmed, ReviewedAtUtc = DateTimeOffset.UtcNow, ReviewedByUserId = "admin" },
            new PaymentNotification { MemberId = 1, Amount = 40m, PaymentMethod = PaymentMethod.Cash, Status = PaymentNotificationStatus.Rejected, ReviewedAtUtc = DateTimeOffset.UtcNow, ReviewedByUserId = "admin" });
        await testContext.DbContext.SaveChangesAsync();

        var service = testContext.CreatePaymentNotificationService();
        var count = await service.GetPendingCountAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ConfirmAsync_PendingNotification_CreatesExactlyOnePaymentAndStoresReviewData()
    {
        await using var testContext = await PaymentNotificationTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, plotId: 101, chargeAmount: 250m);
        await testContext.SeedUserAsync("admin-user", "admin@example.com");

        var notification = new PaymentNotification
        {
            MemberId = 1,
            Amount = 150m,
            PaymentMethod = PaymentMethod.Card,
            Description = "Оплата",
            Status = PaymentNotificationStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        testContext.DbContext.PaymentNotifications.Add(notification);
        await testContext.DbContext.SaveChangesAsync();

        var service = testContext.CreatePaymentNotificationService();
        var result = await service.ConfirmAsync(new ConfirmPaymentNotificationRequest(notification.Id, new DateOnly(2026, 8, 1), 101, "admin-user"));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.PaymentId);

        var storedNotification = await testContext.DbContext.PaymentNotifications.SingleAsync();
        Assert.Equal(PaymentNotificationStatus.Confirmed, storedNotification.Status);
        Assert.Equal(result.PaymentId, storedNotification.PaymentId);
        Assert.Equal("admin-user", storedNotification.ReviewedByUserId);
        Assert.NotNull(storedNotification.ReviewedAtUtc);

        var payments = await testContext.DbContext.Payments.ToListAsync();
        Assert.Single(payments);
        Assert.Equal(101, payments[0].PlotId);
        Assert.Equal(150m, payments[0].Amount);
    }

    [Fact]
    public async Task ConfirmAsync_AlreadyConfirmedNotification_DoesNotCreateSecondPayment()
    {
        await using var testContext = await PaymentNotificationTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, plotId: 101, chargeAmount: 250m);
        await testContext.SeedUserAsync("admin-user", "admin@example.com");

        var notification = new PaymentNotification
        {
            MemberId = 1,
            Amount = 100m,
            PaymentMethod = PaymentMethod.Cash,
            Status = PaymentNotificationStatus.Pending
        };

        testContext.DbContext.PaymentNotifications.Add(notification);
        await testContext.DbContext.SaveChangesAsync();

        var service = testContext.CreatePaymentNotificationService();
        var firstResult = await service.ConfirmAsync(new ConfirmPaymentNotificationRequest(notification.Id, new DateOnly(2026, 8, 1), 101, "admin-user"));
        var secondResult = await service.ConfirmAsync(new ConfirmPaymentNotificationRequest(notification.Id, new DateOnly(2026, 8, 1), 101, "admin-user"));

        Assert.True(firstResult.Succeeded);
        Assert.Equal(PaymentNotificationOperationResultCode.AlreadyProcessed, secondResult.Code);
        Assert.Single(await testContext.DbContext.Payments.ToListAsync());
    }

    [Fact]
    public async Task RejectAsync_PendingNotification_DoesNotCreatePayment_AndCannotBeConfirmedLater()
    {
        await using var testContext = await PaymentNotificationTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, plotId: 101, chargeAmount: 250m);
        await testContext.SeedUserAsync("admin-user", "admin@example.com");

        var notification = new PaymentNotification
        {
            MemberId = 1,
            Amount = 100m,
            PaymentMethod = PaymentMethod.Cash,
            Status = PaymentNotificationStatus.Pending
        };

        testContext.DbContext.PaymentNotifications.Add(notification);
        await testContext.DbContext.SaveChangesAsync();

        var service = testContext.CreatePaymentNotificationService();
        var rejectResult = await service.RejectAsync(new RejectPaymentNotificationRequest(notification.Id, "admin-user", "wrong receipt"));
        var confirmResult = await service.ConfirmAsync(new ConfirmPaymentNotificationRequest(notification.Id, new DateOnly(2026, 8, 1), 101, "admin-user"));

        Assert.True(rejectResult.Succeeded);
        Assert.Equal(PaymentNotificationOperationResultCode.AlreadyProcessed, confirmResult.Code);

        var storedNotification = await testContext.DbContext.PaymentNotifications.SingleAsync();
        Assert.Equal(PaymentNotificationStatus.Rejected, storedNotification.Status);
        Assert.Equal("wrong receipt", storedNotification.AdministratorComment);
        Assert.Empty(await testContext.DbContext.Payments.ToListAsync());
    }

    [Fact]
    public async Task ConfirmAsync_WhenPaymentCreationFails_RollsBackAndLeavesNotificationPending()
    {
        await using var testContext = await PaymentNotificationTestContext.CreateAsync();
        await testContext.SeedMemberAsync(1, "member-user");

        var notification = new PaymentNotification
        {
            MemberId = 1,
            Amount = 100m,
            PaymentMethod = PaymentMethod.Cash,
            Status = PaymentNotificationStatus.Pending
        };

        testContext.DbContext.PaymentNotifications.Add(notification);
        await testContext.DbContext.SaveChangesAsync();

        var service = testContext.CreatePaymentNotificationService(new FailingPaymentService(CreateMemberPaymentResult.Failure(CreateMemberPaymentResultCode.NoEligiblePlots)));
        var result = await service.ConfirmAsync(new ConfirmPaymentNotificationRequest(notification.Id, new DateOnly(2026, 8, 1), null, "admin-user"));

        Assert.Equal(PaymentNotificationOperationResultCode.PaymentCreationFailed, result.Code);

        var storedNotification = await testContext.DbContext.PaymentNotifications.SingleAsync();
        Assert.Equal(PaymentNotificationStatus.Pending, storedNotification.Status);
        Assert.Null(storedNotification.PaymentId);
        Assert.Null(storedNotification.ReviewedAtUtc);
        Assert.Empty(await testContext.DbContext.Payments.ToListAsync());
    }

    private sealed class FailingPaymentService : IPaymentService
    {
        private readonly CreateMemberPaymentResult _result;

        public FailingPaymentService(CreateMemberPaymentResult result)
        {
            _result = result;
        }

        public Task<CreateMemberPaymentResult> CreateMemberPaymentAsync(CreateMemberPaymentRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class PaymentNotificationTestContext : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private PaymentNotificationTestContext(SqliteConnection connection, ApplicationDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
        }

        public ApplicationDbContext DbContext { get; }

        public static async Task<PaymentNotificationTestContext> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            return new PaymentNotificationTestContext(connection, dbContext);
        }

        public PaymentNotificationService CreatePaymentNotificationService(IPaymentService? paymentService = null)
        {
            paymentService ??= new PaymentService(DbContext);
            return new PaymentNotificationService(DbContext, paymentService);
        }

        public async Task SeedMemberAsync(int memberId, string userId, string fullName = "Member")
        {
            DbContext.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = userId,
                NormalizedUserName = userId.ToUpperInvariant(),
                Email = $"{userId}@example.com",
                NormalizedEmail = $"{userId}@example.com".ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString("N"),
                FirstName = fullName,
                LastName = "User"
            });

            DbContext.Members.Add(new Member
            {
                Id = memberId,
                ApplicationUserId = userId,
                FullName = fullName,
                IsActive = true
            });

            await DbContext.SaveChangesAsync();
        }

        public async Task SeedUserAsync(string userId, string email, string displayName = "Admin User")
        {
            if (await DbContext.Users.AnyAsync(item => item.Id == userId))
            {
                return;
            }

            DbContext.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString("N"),
                FirstName = displayName,
                LastName = "User",
                DisplayName = displayName
            });

            await DbContext.SaveChangesAsync();
        }

        public async Task SeedMemberWithPlotAndChargeAsync(int memberId, int plotId, decimal chargeAmount)
        {
            await SeedMemberAsync(memberId, $"member-{memberId}", $"Member {memberId}");

            DbContext.ChargeTypes.Add(new Neftyanik.Portal.Domain.Entities.ChargeType
            {
                Id = 1,
                Name = "Test charge",
                Code = "TEST-CHARGE",
                IsActive = true
            });

            DbContext.Plots.Add(new Plot
            {
                Id = plotId,
                Number = $"P-{plotId}",
                Address = $"Plot {plotId}",
                IsActive = true
            });

            DbContext.PlotOwnerships.Add(new PlotOwnership
            {
                Id = plotId,
                MemberId = memberId,
                PlotId = plotId,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            });

            DbContext.Charges.Add(new Charge
            {
                Id = plotId,
                PlotId = plotId,
                ChargeTypeId = 1,
                Amount = chargeAmount,
                ChargeDate = new DateOnly(2026, 1, 1),
                CreatedAtUtc = DateTime.UtcNow
            });

            await DbContext.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
#endif
