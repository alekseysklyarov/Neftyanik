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

public sealed class ChargeServiceTests
{
    [Fact]
    public async Task CancelChargeAsync_ActiveCharge_PersistsCancellationStateAndWritesAudit()
    {
        await using var testContext = await ChargeServiceTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeTypeAsync(memberId: 1, plotId: 101);
        await testContext.SeedUserAsync("accountant-user", "accountant@example.com", "Portal Accountant");
        testContext.SetCurrentUser("accountant-user", "accountant@example.com");

        var charge = await testContext.SeedChargeAsync(101, 1000m, new DateOnly(2026, 8, 1));
        var payment = await testContext.SeedPaymentAsync(101, 600m, new DateOnly(2026, 8, 2));
        await testContext.SeedPaymentAllocationAsync(payment.Id, charge.Id, 600m);

        var service = testContext.CreateChargeService();
        var result = await service.CancelChargeAsync(new CancelChargeRequest(charge.Id, "Начисление создано ошибочно."));

        Assert.True(result.Succeeded);

        var storedCharge = await testContext.DbContext.Charges.AsNoTracking().SingleAsync();
        Assert.NotNull(storedCharge.CancelledAtUtc);
        Assert.Equal("Начисление создано ошибочно.", storedCharge.CancellationReason);

        var allocations = await testContext.DbContext.PaymentAllocations.AsNoTracking().ToListAsync();
        Assert.Single(allocations);
        Assert.Equal(600m, allocations[0].Amount);

        var auditEntry = await testContext.DbContext.FinancialAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(FinancialAuditLogActions.Cancelled, auditEntry.Action);
        Assert.Equal(nameof(Charge), auditEntry.EntityType);
        Assert.Equal(charge.Id.ToString(), auditEntry.EntityId);
        Assert.Equal("accountant-user", auditEntry.UserId);
        Assert.Equal("accountant@example.com", auditEntry.UserName);
        Assert.Contains("Отменено начисление", auditEntry.Description, StringComparison.Ordinal);
        Assert.Contains("\"ChargeId\":" + charge.Id, auditEntry.OldValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"CancelledAtUtc\":null", auditEntry.OldValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"CancellationReason\":null", auditEntry.OldValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"CancellationReason\":", auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"PaymentId\":" + payment.Id, auditEntry.NewValuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelChargeAsync_InvalidReason_IsRejectedWithoutAudit()
    {
        await using var testContext = await ChargeServiceTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeTypeAsync(memberId: 1, plotId: 101);
        var charge = await testContext.SeedChargeAsync(101, 1000m, new DateOnly(2026, 8, 1));

        var service = testContext.CreateChargeService();

        var whitespaceResult = await service.CancelChargeAsync(new CancelChargeRequest(charge.Id, "   "));
        var tooLongResult = await service.CancelChargeAsync(new CancelChargeRequest(charge.Id, new string('x', 501)));

        Assert.Equal(CancelChargeResultCode.InvalidCancellationReason, whitespaceResult.Code);
        Assert.Equal(CancelChargeResultCode.InvalidCancellationReason, tooLongResult.Code);
        Assert.Empty(await testContext.DbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());

        var storedCharge = await testContext.DbContext.Charges.AsNoTracking().SingleAsync();
        Assert.Null(storedCharge.CancelledAtUtc);
        Assert.Null(storedCharge.CancellationReason);
    }

    [Fact]
    public async Task CancelChargeAsync_AlreadyCancelledCharge_IsRejectedWithoutDuplicateAudit()
    {
        await using var testContext = await ChargeServiceTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeTypeAsync(memberId: 1, plotId: 101);
        var cancelledAtUtc = DateTime.UtcNow.AddDays(-1);
        var charge = await testContext.SeedChargeAsync(101, 1000m, new DateOnly(2026, 8, 1), cancelledAtUtc, "Уже отменено");

        var service = testContext.CreateChargeService();
        var result = await service.CancelChargeAsync(new CancelChargeRequest(charge.Id, "Повторная отмена"));

        Assert.Equal(CancelChargeResultCode.AlreadyCancelled, result.Code);
        Assert.Empty(await testContext.DbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());

        var storedCharge = await testContext.DbContext.Charges.AsNoTracking().SingleAsync();
        Assert.Equal(cancelledAtUtc, storedCharge.CancelledAtUtc);
        Assert.Equal("Уже отменено", storedCharge.CancellationReason);
    }

    [Fact]
    public async Task CancelChargeAsync_ElectricityLinkedCharge_PreservesReading()
    {
        await using var testContext = await ChargeServiceTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeTypeAsync(memberId: 1, plotId: 101);
        await testContext.SeedUserAsync("admin-user", "admin@example.com", "Portal Admin");
        testContext.SetCurrentUser("admin-user", "admin@example.com");

        var charge = await testContext.SeedChargeAsync(101, 125m, new DateOnly(2026, 8, 1));
        var reading = await testContext.SeedElectricityReadingAsync(memberId: 1, plotId: 101, chargeId: charge.Id);

        var service = testContext.CreateChargeService();
        var result = await service.CancelChargeAsync(new CancelChargeRequest(charge.Id, "Корректировка начисления по показаниям."));

        Assert.True(result.Succeeded);

        var storedReading = await testContext.DbContext.MemberElectricityReadings.AsNoTracking().SingleAsync();
        Assert.Equal(reading.Id, storedReading.Id);
        Assert.Equal(charge.Id, storedReading.ChargeId);
        Assert.Equal(150m, storedReading.CurrentReading);
        Assert.Equal(125m, storedReading.Amount);
        Assert.Equal(5.00m, storedReading.AppliedMemberRate);
        Assert.Equal(2.50m, storedReading.AppliedMemberNightRate);

        var auditEntry = await testContext.DbContext.FinancialAuditLogs.AsNoTracking().SingleAsync();
        Assert.Contains("\"MemberElectricityReadingId\":" + reading.Id, auditEntry.NewValuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelChargeAsync_WhenAuditAddFails_DoesNotPersistCancellationState()
    {
        await using var testContext = await ChargeServiceTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeTypeAsync(memberId: 1, plotId: 101);
        var charge = await testContext.SeedChargeAsync(101, 1000m, new DateOnly(2026, 8, 1));

        var service = new ChargeService(testContext.DbContext, new ThrowingFinancialAuditService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelChargeAsync(new CancelChargeRequest(charge.Id, "Причина отмены")));

        testContext.DbContext.ChangeTracker.Clear();

        var storedCharge = await testContext.DbContext.Charges.AsNoTracking().SingleAsync();
        Assert.Null(storedCharge.CancelledAtUtc);
        Assert.Null(storedCharge.CancellationReason);
        Assert.Empty(await testContext.DbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CancelChargeAsync_CancelledCharge_IsIgnoredForFuturePaymentAllocations()
    {
        await using var testContext = await ChargeServiceTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeTypeAsync(memberId: 1, plotId: 101);
        await testContext.SeedUserAsync("accountant-user", "accountant@example.com", "Portal Accountant");
        testContext.SetCurrentUser("accountant-user", "accountant@example.com");

        var cancelledCharge = await testContext.SeedChargeAsync(101, 100m, new DateOnly(2026, 8, 1));
        var activeCharge = await testContext.SeedChargeAsync(101, 400m, new DateOnly(2026, 8, 2));

        var chargeService = testContext.CreateChargeService();
        var cancelResult = await chargeService.CancelChargeAsync(new CancelChargeRequest(cancelledCharge.Id, "Ошибка ввода начисления."));
        Assert.True(cancelResult.Succeeded);

        var paymentService = testContext.CreatePaymentService();
        var paymentResult = await paymentService.CreateMemberPaymentAsync(new CreateMemberPaymentRequest(
            1,
            101,
            new DateOnly(2026, 8, 3),
            150m,
            PaymentMethod.Cash,
            "RCPT-100",
            "Оплата после отмены начисления",
            "accountant-user"));

        Assert.True(paymentResult.Succeeded);

        var allocations = await testContext.DbContext.PaymentAllocations
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .ToListAsync();

        Assert.Single(allocations);
        Assert.Equal(activeCharge.Id, allocations[0].ChargeId);
        Assert.Equal(150m, allocations[0].Amount);
    }

    private sealed class ThrowingFinancialAuditService : IFinancialAuditService
    {
        public void Add(string action, string entityType, string entityId, string? description = null, object? oldValues = null, object? newValues = null)
        {
            throw new InvalidOperationException("Audit failure");
        }
    }

    private sealed class ChargeServiceTestContext : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly HttpContextAccessor _httpContextAccessor;

        private ChargeServiceTestContext(SqliteConnection connection, ApplicationDbContext dbContext, HttpContextAccessor httpContextAccessor)
        {
            _connection = connection;
            DbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
        }

        public ApplicationDbContext DbContext { get; }

        public static async Task<ChargeServiceTestContext> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            return new ChargeServiceTestContext(connection, dbContext, new HttpContextAccessor());
        }

        public ChargeService CreateChargeService()
        {
            return new ChargeService(DbContext, new FinancialAuditService(DbContext, _httpContextAccessor));
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

        public async Task SeedMemberWithPlotAndChargeTypeAsync(int memberId, int plotId)
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

            DbContext.ChargeTypes.Add(new Neftyanik.Portal.Domain.Entities.ChargeType
            {
                Id = 1,
                Name = "Test charge",
                Code = "TEST-CHARGE",
                IsActive = true
            });

            await DbContext.SaveChangesAsync();
        }

        public async Task<Charge> SeedChargeAsync(int plotId, decimal amount, DateOnly chargeDate, DateTime? cancelledAtUtc = null, string? cancellationReason = null)
        {
            var charge = new Charge
            {
                PlotId = plotId,
                ChargeTypeId = 1,
                Amount = amount,
                ChargeDate = chargeDate,
                CreatedAtUtc = DateTime.UtcNow,
                CancelledAtUtc = cancelledAtUtc,
                CancellationReason = cancellationReason
            };

            DbContext.Charges.Add(charge);
            await DbContext.SaveChangesAsync();
            return charge;
        }

        public async Task<Payment> SeedPaymentAsync(int plotId, decimal amount, DateOnly paymentDate, DateTime? cancelledAtUtc = null)
        {
            var payment = new Payment
            {
                PlotId = plotId,
                PaymentDate = paymentDate,
                Amount = amount,
                PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = DateTime.UtcNow,
                CancelledAtUtc = cancelledAtUtc
            };

            DbContext.Payments.Add(payment);
            await DbContext.SaveChangesAsync();
            return payment;
        }

        public async Task SeedPaymentAllocationAsync(long paymentId, long chargeId, decimal amount)
        {
            DbContext.PaymentAllocations.Add(new PaymentAllocation
            {
                PaymentId = paymentId,
                ChargeId = chargeId,
                Amount = amount
            });
            await DbContext.SaveChangesAsync();
        }

        public async Task<MemberElectricityReading> SeedElectricityReadingAsync(int memberId, int plotId, long chargeId)
        {
            var meter = new MemberElectricityMeter
            {
                Id = plotId + 1000,
                MemberId = memberId,
                BillingPlotId = plotId,
                Name = $"Meter-{plotId}",
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            DbContext.MemberElectricityMeters.Add(meter);
            await DbContext.SaveChangesAsync();

            var reading = new MemberElectricityReading
            {
                MemberElectricityMeterId = meter.Id,
                ReadingDate = new DateOnly(2026, 8, 1),
                CurrentReading = 150m,
                CurrentNightReading = 40m,
                AppliedMemberRate = 5.00m,
                AppliedMemberNightRate = 2.50m,
                Amount = 125m,
                ChargeId = chargeId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            DbContext.MemberElectricityReadings.Add(reading);
            await DbContext.SaveChangesAsync();
            return reading;
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
