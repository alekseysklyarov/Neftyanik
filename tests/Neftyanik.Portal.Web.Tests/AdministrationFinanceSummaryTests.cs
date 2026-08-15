using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using FinanceIndexModel = Neftyanik.Portal.Web.Pages.Administration.Finance.IndexModel;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationFinanceSummaryTests
{
    [Fact]
    public async Task OnGetAsync_UsesInitializationCutoffForCurrentFundsAndOpeningBalance()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var currentYear = DateTime.Today.Year;
        var yearStart = new DateOnly(currentYear, 1, 1);
        var acceptedAt = new DateOnly(currentYear, 7, 1);
        var beforeInitialization = acceptedAt.AddDays(-1);
        var afterInitialization = acceptedAt.AddDays(10);

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "system",
            UserName = "system@example.com",
            NormalizedUserName = "SYSTEM@EXAMPLE.COM",
            Email = "system@example.com",
            NormalizedEmail = "SYSTEM@EXAMPLE.COM",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            FirstName = "System",
            LastName = "User",
            IsActive = true
        });

        dbContext.Members.Add(new Member
        {
            Id = 1,
            FullName = "Finance Summary Member",
            IsActive = true
        });

        dbContext.Plots.Add(new Plot
        {
            Id = 1,
            Number = "P-1",
            IsActive = true
        });

        dbContext.PlotOwnerships.Add(new PlotOwnership
        {
            Id = 1,
            MemberId = 1,
            PlotId = 1,
            ValidFrom = new DateOnly(2020, 1, 1),
            IsPrimaryContact = true
        });

        dbContext.ChargeTypes.Add(new Neftyanik.Portal.Domain.Entities.ChargeType
        {
            Id = 1,
            Code = "test",
            Name = "Тестовое начисление",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        dbContext.Charges.AddRange(
            new Charge
            {
                Id = 1,
                PlotId = 1,
                ChargeTypeId = 1,
                Amount = 100m,
                ChargeDate = beforeInitialization,
                CreatedAtUtc = DateTime.UtcNow
            },
            new Charge
            {
                Id = 2,
                PlotId = 1,
                ChargeTypeId = 1,
                Amount = 100m,
                ChargeDate = afterInitialization,
                CreatedAtUtc = DateTime.UtcNow
            });

        dbContext.Payments.AddRange(
            new Payment
            {
                Id = 1,
                PlotId = 1,
                Amount = 40m,
                PaymentDate = beforeInitialization,
                PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = DateTime.UtcNow
            },
            new Payment
            {
                Id = 2,
                PlotId = 1,
                Amount = 20m,
                PaymentDate = afterInitialization,
                PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = DateTime.UtcNow
            },
            new Payment
            {
                Id = 3,
                PlotId = 1,
                Amount = 50m,
                PaymentDate = afterInitialization,
                PaymentMethod = PaymentMethod.Card,
                CreatedAtUtc = DateTime.UtcNow
            },
            new Payment
            {
                Id = 4,
                PlotId = 1,
                Amount = 15m,
                PaymentDate = beforeInitialization,
                PaymentMethod = PaymentMethod.Card,
                CreatedAtUtc = DateTime.UtcNow
            });

        dbContext.Expenses.AddRange(
            new Expense
            {
                Id = 1,
                ExpenseCategoryId = 1,
                ExpenseDate = beforeInitialization,
                Amount = 10m,
                Description = "Expense before initialization",
                CreatedByUserId = "system",
                CreatedAt = DateTimeOffset.UtcNow
            },
            new Expense
            {
                Id = 2,
                ExpenseCategoryId = 1,
                ExpenseDate = afterInitialization,
                Amount = 5m,
                Description = "Expense after initialization",
                CreatedByUserId = "system",
                CreatedAt = DateTimeOffset.UtcNow
            });

        dbContext.SystemSettings.Add(new SystemSetting
        {
            Id = 1,
            Key = "Finance.CashInitialization",
            Value = "{\"Amount\":25,\"AcceptedAt\":\"" + acceptedAt.ToString("yyyy-MM-dd") + "\",\"AcceptedFrom\":\"Кассир\",\"AdvancePaymentsAmount\":10}",
            Description = "Initial cash amount configured from finance settings.",
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedByUserId = "system"
        });

        await dbContext.SaveChangesAsync();

        var model = new FinanceIndexModel(dbContext);

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(currentYear, model.CurrentYear);
        Assert.Equal(80m, model.Summary.CurrentCashAmount);
        Assert.Equal(30m, model.Summary.CurrentCashOnlyAmount);
        Assert.Equal(50m, model.Summary.CurrentNonCashAmount);
        Assert.Equal(0m, model.Summary.OpeningYearCashAmount);
        Assert.Equal(200m, model.Summary.CurrentYearCharges);
        Assert.Equal(0m, model.Summary.OpeningYearDebt);
        Assert.Equal(75m, model.Summary.CurrentYearDebt);
        Assert.Equal(0m, model.Summary.TotalOverpayments);
    }
}
