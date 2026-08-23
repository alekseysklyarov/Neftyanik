#if WEB_TESTS
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Application.Payments;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public sealed class PaymentServiceTests
{
    [Fact]
    public async Task CreateMemberPaymentAsync_ManualPayment_CreatesOnePaymentAuditEntry()
    {
        await using var testContext = await PaymentServiceTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, plotId: 101, chargeAmount: 250m);
        await testContext.SeedUserAsync("accountant-user", "accountant@example.com", "Portal Accountant");
        testContext.SetCurrentUser("accountant-user", "accountant@example.com");

        var service = testContext.CreatePaymentService();
        var result = await service.CreateMemberPaymentAsync(new CreateMemberPaymentRequest(
            1,
            101,
            new DateOnly(2026, 8, 1),
            150m,
            PaymentMethod.Cash,
            "RCPT-1",
            "Ручной платёж",
            "accountant-user"));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.PaymentId);

        var payment = await testContext.DbContext.Payments.SingleAsync();
        Assert.Equal(150m, payment.Amount);

        var auditEntry = await testContext.DbContext.FinancialAuditLogs.SingleAsync();
        Assert.Equal(FinancialAuditLogActions.Created, auditEntry.Action);
        Assert.Equal(nameof(Payment), auditEntry.EntityType);
        Assert.Equal(payment.Id.ToString(), auditEntry.EntityId);
        Assert.Equal("accountant-user", auditEntry.UserId);
        Assert.Equal("accountant@example.com", auditEntry.UserName);
        Assert.Contains("Создан платеж", auditEntry.Description, StringComparison.Ordinal);
        Assert.Contains("\"MemberId\":1", auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"PaymentDate\":\"2026-08-01\"", auditEntry.NewValuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateMemberPaymentAsync_WhenAuditAddFails_RollsBackPaymentAndAllocations()
    {
        await using var testContext = await PaymentServiceTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, plotId: 101, chargeAmount: 250m);
        await testContext.SeedUserAsync("accountant-user", "accountant@example.com", "Portal Accountant");
        testContext.SetCurrentUser("accountant-user", "accountant@example.com");

        var service = new PaymentService(testContext.DbContext, new ThrowingFinancialAuditService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateMemberPaymentAsync(new CreateMemberPaymentRequest(
            1,
            101,
            new DateOnly(2026, 8, 1),
            150m,
            PaymentMethod.Cash,
            null,
            "Ручной платёж",
            "accountant-user")));

        Assert.Empty(await testContext.DbContext.Payments.AsNoTracking().ToListAsync());
        Assert.Empty(await testContext.DbContext.PaymentAllocations.AsNoTracking().ToListAsync());
        Assert.Empty(await testContext.DbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CancelPaymentAsync_ActivePayment_PersistsCancellationStatePreservesAllocationsAndWritesAudit()
    {
        await using var testContext = await PaymentServiceTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, plotId: 101, chargeAmount: 250m);
        await testContext.SeedUserAsync("accountant-user", "accountant@example.com", "Portal Accountant");
        testContext.SetCurrentUser("accountant-user", "accountant@example.com");

        var payment = await testContext.SeedPaymentAsync(
            plotId: 101,
            amount: 150m,
            paymentDate: new DateOnly(2026, 8, 1),
            cancellationReason: null,
            cancelledAtUtc: null,
            allocations: [(101L, 100m)]);

        var service = testContext.CreatePaymentService();
        var result = await service.CancelPaymentAsync(new CancelPaymentRequest(payment.Id, "Платеж внесен вручную ошибочно."));

        Assert.True(result.Succeeded);

        var storedPayment = await testContext.DbContext.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(payment.Id, storedPayment.Id);
        Assert.NotNull(storedPayment.CancelledAtUtc);
        Assert.Equal("Платеж внесен вручную ошибочно.", storedPayment.CancellationReason);

        var allocations = await testContext.DbContext.PaymentAllocations.AsNoTracking().ToListAsync();
        Assert.Single(allocations);
        Assert.Equal(100m, allocations[0].Amount);

        var auditEntry = await testContext.DbContext.FinancialAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(FinancialAuditLogActions.Cancelled, auditEntry.Action);
        Assert.Equal(nameof(Payment), auditEntry.EntityType);
        Assert.Equal(payment.Id.ToString(), auditEntry.EntityId);
        Assert.Equal("accountant-user", auditEntry.UserId);
        Assert.Equal("accountant@example.com", auditEntry.UserName);
        Assert.Contains("Отменен платеж", auditEntry.Description, StringComparison.Ordinal);
        Assert.Contains("\"PaymentId\":" + payment.Id, auditEntry.OldValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"CancelledAtUtc\":null", auditEntry.OldValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"CancellationReason\":null", auditEntry.OldValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"CancellationReason\":", auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"ChargeId\":101", auditEntry.NewValuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelPaymentAsync_WhitespaceReason_IsRejected()
    {
        await using var testContext = await PaymentServiceTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, plotId: 101, chargeAmount: 250m);
        var payment = await testContext.SeedPaymentAsync(
            plotId: 101,
            amount: 150m,
            paymentDate: new DateOnly(2026, 8, 1),
            cancellationReason: null,
            cancelledAtUtc: null,
            allocations: [(101L, 100m)]);

        var service = testContext.CreatePaymentService();
        var result = await service.CancelPaymentAsync(new CancelPaymentRequest(payment.Id, "   "));

        Assert.Equal(CancelPaymentResultCode.InvalidCancellationReason, result.Code);
        Assert.Empty(await testContext.DbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());

        var storedPayment = await testContext.DbContext.Payments.AsNoTracking().SingleAsync();
        Assert.Null(storedPayment.CancelledAtUtc);
        Assert.Null(storedPayment.CancellationReason);
    }

    [Fact]
    public async Task CancelPaymentAsync_AlreadyCancelledPayment_IsRejectedWithoutDuplicateAudit()
    {
        await using var testContext = await PaymentServiceTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, plotId: 101, chargeAmount: 250m);
        var cancelledAtUtc = DateTime.UtcNow.AddDays(-1);
        var payment = await testContext.SeedPaymentAsync(
            plotId: 101,
            amount: 150m,
            paymentDate: new DateOnly(2026, 8, 1),
            cancellationReason: "Уже отменен",
            cancelledAtUtc: cancelledAtUtc,
            allocations: [(101L, 100m)]);

        var service = testContext.CreatePaymentService();
        var result = await service.CancelPaymentAsync(new CancelPaymentRequest(payment.Id, "Повторная отмена"));

        Assert.Equal(CancelPaymentResultCode.AlreadyCancelled, result.Code);
        Assert.Empty(await testContext.DbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());

        var storedPayment = await testContext.DbContext.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(cancelledAtUtc, storedPayment.CancelledAtUtc);
        Assert.Equal("Уже отменен", storedPayment.CancellationReason);
    }

    [Fact]
    public async Task CancelPaymentAsync_CancelledAllocationsAreIgnoredForFutureOutstandingBalance()
    {
        await using var testContext = await PaymentServiceTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, plotId: 101, chargeAmount: 250m);
        await testContext.SeedUserAsync("accountant-user", "accountant@example.com", "Portal Accountant");
        testContext.SetCurrentUser("accountant-user", "accountant@example.com");

        var service = testContext.CreatePaymentService();
        var firstPayment = await service.CreateMemberPaymentAsync(new CreateMemberPaymentRequest(
            1,
            101,
            new DateOnly(2026, 8, 1),
            150m,
            PaymentMethod.Cash,
            "RCPT-1",
            "Первый платеж",
            "accountant-user"));

        Assert.True(firstPayment.Succeeded);

        var cancelResult = await service.CancelPaymentAsync(new CancelPaymentRequest(firstPayment.PaymentId!.Value, "Платеж внесен ошибочно."));
        Assert.True(cancelResult.Succeeded);

        var secondPayment = await service.CreateMemberPaymentAsync(new CreateMemberPaymentRequest(
            1,
            101,
            new DateOnly(2026, 8, 2),
            150m,
            PaymentMethod.Cash,
            "RCPT-2",
            "Повторный платеж",
            "accountant-user"));

        Assert.True(secondPayment.Succeeded);
        Assert.Equal(150m, secondPayment.AllocatedAmount);
        Assert.Equal(0m, secondPayment.AdvanceAmount);

        var allocations = await testContext.DbContext.PaymentAllocations
            .AsNoTracking()
            .OrderBy(item => item.PaymentId)
            .ThenBy(item => item.ChargeId)
            .ToListAsync();

        Assert.Equal(2, allocations.Count);
        Assert.All(allocations, allocation => Assert.Equal(150m, allocation.Amount));
    }

    [Fact]
    public async Task CancelPaymentAsync_PaymentCreatedFromNotification_KeepsNotificationConfirmedAndLinked()
    {
        await using var testContext = await PaymentServiceTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, plotId: 101, chargeAmount: 250m);
        await testContext.SeedUserAsync("admin-user", "admin@example.com", "Portal Admin");
        testContext.SetCurrentUser("admin-user", "admin@example.com");

        var payment = await testContext.SeedPaymentAsync(
            plotId: 101,
            amount: 150m,
            paymentDate: new DateOnly(2026, 8, 1),
            cancellationReason: null,
            cancelledAtUtc: null,
            allocations: [(101L, 100m)]);

        testContext.DbContext.PaymentNotifications.Add(new PaymentNotification
        {
            Id = 2001,
            MemberId = 1,
            Amount = 150m,
            PaymentMethod = PaymentMethod.Card,
            Status = PaymentNotificationStatus.Confirmed,
            PaymentId = payment.Id,
            ReviewedAtUtc = DateTimeOffset.UtcNow,
            ReviewedByUserId = "admin-user"
        });
        await testContext.DbContext.SaveChangesAsync();

        var service = testContext.CreatePaymentService();
        var result = await service.CancelPaymentAsync(new CancelPaymentRequest(payment.Id, "Отмена связанного платежа."));

        Assert.True(result.Succeeded);

        var storedNotification = await testContext.DbContext.PaymentNotifications.AsNoTracking().SingleAsync();
        Assert.Equal(PaymentNotificationStatus.Confirmed, storedNotification.Status);
        Assert.Equal(payment.Id, storedNotification.PaymentId);

        var storedPayment = await testContext.DbContext.Payments.AsNoTracking().SingleAsync();
        Assert.NotNull(storedPayment.CancelledAtUtc);
    }

    [Fact]
    public async Task CancelPaymentAsync_WhenAuditAddFails_DoesNotPersistCancellationState()
    {
        await using var testContext = await PaymentServiceTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, plotId: 101, chargeAmount: 250m);
        var payment = await testContext.SeedPaymentAsync(
            plotId: 101,
            amount: 150m,
            paymentDate: new DateOnly(2026, 8, 1),
            cancellationReason: null,
            cancelledAtUtc: null,
            allocations: [(101L, 100m)]);

        var service = new PaymentService(testContext.DbContext, new ThrowingFinancialAuditService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelPaymentAsync(new CancelPaymentRequest(payment.Id, "Причина отмены")));

        testContext.DbContext.ChangeTracker.Clear();

        var storedPayment = await testContext.DbContext.Payments.AsNoTracking().SingleAsync();
        Assert.Null(storedPayment.CancelledAtUtc);
        Assert.Null(storedPayment.CancellationReason);
        Assert.Single(await testContext.DbContext.PaymentAllocations.AsNoTracking().ToListAsync());
        Assert.Empty(await testContext.DbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
    }

    private sealed class ThrowingFinancialAuditService : IFinancialAuditService
    {
        public void Add(string action, string entityType, string entityId, string? description = null, object? oldValues = null, object? newValues = null)
        {
            throw new InvalidOperationException("Audit failure");
        }
    }

    private sealed class PaymentServiceTestContext : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly HttpContextAccessor _httpContextAccessor;

        private PaymentServiceTestContext(SqliteConnection connection, ApplicationDbContext dbContext, HttpContextAccessor httpContextAccessor)
        {
            _connection = connection;
            DbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
        }

        public ApplicationDbContext DbContext { get; }

        public static async Task<PaymentServiceTestContext> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            return new PaymentServiceTestContext(connection, dbContext, new HttpContextAccessor());
        }

        public PaymentService CreatePaymentService()
        {
            return new PaymentService(DbContext, new FinancialAuditService(DbContext, _httpContextAccessor));
        }

        public void SetCurrentUser(string userId, string userName)
        {
            _httpContextAccessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Name, userName)
                ],
                authenticationType: "TestAuthentication"))
            };
        }

        public async Task SeedMemberWithPlotAndChargeAsync(int memberId, int plotId, decimal chargeAmount)
        {
            DbContext.Users.Add(new ApplicationUser
            {
                Id = $"member-{memberId}",
                UserName = $"member-{memberId}@example.com",
                NormalizedUserName = $"MEMBER-{memberId}@EXAMPLE.COM",
                Email = $"member-{memberId}@example.com",
                NormalizedEmail = $"MEMBER-{memberId}@EXAMPLE.COM",
                SecurityStamp = Guid.NewGuid().ToString("N"),
                FirstName = "Member",
                LastName = memberId.ToString()
            });

            DbContext.Members.Add(new Member
            {
                Id = memberId,
                ApplicationUserId = $"member-{memberId}",
                FullName = $"Member {memberId}",
                IsActive = true
            });

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

        public async Task<Payment> SeedPaymentAsync(
            int plotId,
            decimal amount,
            DateOnly paymentDate,
            string? cancellationReason,
            DateTime? cancelledAtUtc,
            params (long ChargeId, decimal Amount)[] allocations)
        {
            var payment = new Payment
            {
                PlotId = plotId,
                PaymentDate = paymentDate,
                Amount = amount,
                PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = DateTime.UtcNow,
                CancellationReason = cancellationReason,
                CancelledAtUtc = cancelledAtUtc
            };

            DbContext.Payments.Add(payment);
            await DbContext.SaveChangesAsync();

            if (allocations.Length > 0)
            {
                DbContext.PaymentAllocations.AddRange(allocations.Select(allocation => new PaymentAllocation
                {
                    PaymentId = payment.Id,
                    ChargeId = allocation.ChargeId,
                    Amount = allocation.Amount
                }));
                await DbContext.SaveChangesAsync();
            }

            return payment;
        }

        public async Task SeedUserAsync(string userId, string email, string displayName)
        {
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

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
#endif
