using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests.Data.Queries;

public sealed class MemberFinanceBalanceQueriesTests
{
    [Fact]
    public async Task CalculateActiveBalanceAsync_NoChargesOrPayments_ReturnsZero()
    {
        await using var testContext = await BalanceQueryTestContext.CreateAsync();
        await testContext.SeedMemberPlotAndChargeTypeAsync(memberId: 1, plotId: 101);

        var balance = await testContext.DbContext.CalculateActiveBalanceAsync(1, [101]);

        Assert.Equal(0m, balance);
    }

    [Fact]
    public async Task CalculateActiveBalanceAsync_ChargesOnly_ReturnsPositiveDebt()
    {
        await using var testContext = await BalanceQueryTestContext.CreateAsync();
        await testContext.SeedMemberPlotAndChargeTypeAsync(memberId: 1, plotId: 101);
        testContext.DbContext.Charges.Add(new Charge
        {
            Id = 1001,
            PlotId = 101,
            ChargeTypeId = 1,
            Amount = 250m,
            ChargeDate = new DateOnly(2026, 1, 1)
        });
        await testContext.DbContext.SaveChangesAsync();

        var balance = await testContext.DbContext.CalculateActiveBalanceAsync(1, [101]);

        Assert.Equal(250m, balance);
    }

    [Fact]
    public async Task CalculateActiveBalanceAsync_PaymentReducesDebt()
    {
        await using var testContext = await BalanceQueryTestContext.CreateAsync();
        await testContext.SeedMemberPlotAndChargeTypeAsync(memberId: 1, plotId: 101);
        testContext.DbContext.Charges.Add(new Charge
        {
            Id = 1002,
            PlotId = 101,
            ChargeTypeId = 1,
            Amount = 250m,
            ChargeDate = new DateOnly(2026, 1, 1)
        });
        testContext.DbContext.Payments.Add(new Payment
        {
            Id = 2001,
            MemberId = 1,
            PlotId = 101,
            PaymentDate = new DateOnly(2026, 1, 2),
            Amount = 90m,
            PaymentMethod = PaymentMethod.Cash
        });
        await testContext.DbContext.SaveChangesAsync();

        var balance = await testContext.DbContext.CalculateActiveBalanceAsync(1, [101]);

        Assert.Equal(160m, balance);
    }

    [Fact]
    public async Task CalculateActiveBalanceAsync_PaymentLargerThanDebt_ReturnsNegativeOverpayment()
    {
        await using var testContext = await BalanceQueryTestContext.CreateAsync();
        await testContext.SeedMemberPlotAndChargeTypeAsync(memberId: 1, plotId: 101);
        testContext.DbContext.Charges.Add(new Charge
        {
            Id = 1003,
            PlotId = 101,
            ChargeTypeId = 1,
            Amount = 100m,
            ChargeDate = new DateOnly(2026, 1, 1)
        });
        testContext.DbContext.Payments.Add(new Payment
        {
            Id = 2002,
            MemberId = 1,
            PlotId = 101,
            PaymentDate = new DateOnly(2026, 1, 2),
            Amount = 150m,
            PaymentMethod = PaymentMethod.Card
        });
        await testContext.DbContext.SaveChangesAsync();

        var balance = await testContext.DbContext.CalculateActiveBalanceAsync(1, [101]);

        Assert.Equal(-50m, balance);
    }

    [Fact]
    public async Task CalculateActiveBalanceAsync_CancelledCharge_IsIgnored()
    {
        await using var testContext = await BalanceQueryTestContext.CreateAsync();
        await testContext.SeedMemberPlotAndChargeTypeAsync(memberId: 1, plotId: 101);
        testContext.DbContext.Charges.AddRange(
            new Charge
            {
                Id = 1004,
                PlotId = 101,
                ChargeTypeId = 1,
                Amount = 100m,
                ChargeDate = new DateOnly(2026, 1, 1)
            },
            new Charge
            {
                Id = 1005,
                PlotId = 101,
                ChargeTypeId = 1,
                Amount = 70m,
                ChargeDate = new DateOnly(2026, 1, 2),
                CancelledAtUtc = DateTime.UtcNow
            });
        await testContext.DbContext.SaveChangesAsync();

        var balance = await testContext.DbContext.CalculateActiveBalanceAsync(1, [101]);

        Assert.Equal(100m, balance);
    }

    [Fact]
    public async Task CalculateActiveBalanceAsync_CancelledPayment_IsIgnored()
    {
        await using var testContext = await BalanceQueryTestContext.CreateAsync();
        await testContext.SeedMemberPlotAndChargeTypeAsync(memberId: 1, plotId: 101);
        testContext.DbContext.Charges.Add(new Charge
        {
            Id = 1006,
            PlotId = 101,
            ChargeTypeId = 1,
            Amount = 200m,
            ChargeDate = new DateOnly(2026, 1, 1)
        });
        testContext.DbContext.Payments.AddRange(
            new Payment
            {
                Id = 2003,
                MemberId = 1,
                PlotId = 101,
                PaymentDate = new DateOnly(2026, 1, 2),
                Amount = 80m,
                PaymentMethod = PaymentMethod.BankTransfer
            },
            new Payment
            {
                Id = 2004,
                MemberId = 1,
                PlotId = 101,
                PaymentDate = new DateOnly(2026, 1, 3),
                Amount = 50m,
                PaymentMethod = PaymentMethod.Cash,
                CancelledAtUtc = DateTime.UtcNow
            });
        await testContext.DbContext.SaveChangesAsync();

        var balance = await testContext.DbContext.CalculateActiveBalanceAsync(1, [101]);

        Assert.Equal(120m, balance);
    }

    [Fact]
    public async Task CalculateActiveBalanceAsync_UnallocatedPaymentCredit_RemainsNegativeBalance()
    {
        await using var testContext = await BalanceQueryTestContext.CreateAsync();
        await testContext.SeedMemberPlotAndChargeTypeAsync(memberId: 1, plotId: 101);
        testContext.DbContext.Charges.Add(new Charge
        {
            Id = 1007,
            PlotId = 101,
            ChargeTypeId = 1,
            Amount = 100m,
            ChargeDate = new DateOnly(2026, 1, 1)
        });
        testContext.DbContext.Payments.Add(new Payment
        {
            Id = 2005,
            MemberId = 1,
            PlotId = 101,
            PaymentDate = new DateOnly(2026, 1, 2),
            Amount = 150m,
            PaymentMethod = PaymentMethod.Cash
        });
        testContext.DbContext.PaymentAllocations.Add(new PaymentAllocation
        {
            Id = 3001,
            PaymentId = 2005,
            ChargeId = 1007,
            Amount = 100m
        });
        await testContext.DbContext.SaveChangesAsync();

        var balance = await testContext.DbContext.CalculateActiveBalanceAsync(1, [101]);

        Assert.Equal(-50m, balance);
    }

    private sealed class BalanceQueryTestContext : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private BalanceQueryTestContext(SqliteConnection connection, ApplicationDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
        }

        public ApplicationDbContext DbContext { get; }

        public static async Task<BalanceQueryTestContext> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            return new BalanceQueryTestContext(connection, dbContext);
        }

        public async Task SeedMemberPlotAndChargeTypeAsync(int memberId, int plotId)
        {
            DbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = $"Member {memberId}",
                IsActive = true
            });

            DbContext.Plots.Add(new Plot
            {
                Id = plotId,
                Number = $"P-{plotId}",
                IsActive = true
            });

            DbContext.ChargeTypes.Add(new Neftyanik.Portal.Domain.Entities.ChargeType
            {
                Id = 1,
                Name = "Test charge",
                Code = "TEST",
                IsActive = true
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
