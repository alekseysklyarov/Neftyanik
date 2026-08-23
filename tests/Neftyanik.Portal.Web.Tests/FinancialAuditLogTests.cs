using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using AuditLogIndexModel = Neftyanik.Portal.Web.Pages.Administration.FinancialAuditLog.IndexModel;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class FinancialAuditLogTests
{
    [Fact]
    public async Task Add_TracksAuditEntryWithoutPersistingUntilSaveChangesAsync()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "admin-1"),
                    new Claim(ClaimTypes.Name, "admin@example.com")
                ],
                authenticationType: "TestAuthentication"))
            }
        };

        var service = new FinancialAuditService(dbContext, httpContextAccessor);

        service.Add(
            "Create",
            "Payment",
            "15",
            "Создан тестовый платёж.",
            oldValues: new { Status = "Draft" },
            newValues: new { Status = "Confirmed", Amount = 125.50m });

        var trackedEntry = Assert.Single(dbContext.ChangeTracker.Entries<FinancialAuditLog>());
        Assert.Equal(EntityState.Added, trackedEntry.State);
        Assert.Equal(0, await dbContext.FinancialAuditLogs.AsNoTracking().CountAsync());

        var auditEntry = trackedEntry.Entity;
        Assert.Equal("admin-1", auditEntry.UserId);
        Assert.Equal("admin@example.com", auditEntry.UserName);
        Assert.Equal("Create", auditEntry.Action);
        Assert.Equal("Payment", auditEntry.EntityType);
        Assert.Equal("15", auditEntry.EntityId);
        Assert.Equal("Создан тестовый платёж.", auditEntry.Description);
        Assert.Contains("Draft", auditEntry.OldValuesJson, StringComparison.Ordinal);
        Assert.Contains("Confirmed", auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("125.50", auditEntry.NewValuesJson, StringComparison.Ordinal);

        await dbContext.SaveChangesAsync();

        var persistedEntry = await dbContext.FinancialAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal("admin-1", persistedEntry.UserId);
        Assert.Equal("admin@example.com", persistedEntry.UserName);
        Assert.Equal("Create", persistedEntry.Action);
    }

    [Fact]
    public async Task OnGetAsync_AppliesFiltersAndOrdersNewestEntriesFirst()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.FinancialAuditLogs.AddRange(
            new FinancialAuditLog
            {
                CreatedAtUtc = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc),
                UserId = "admin-1",
                UserName = "chief.accountant",
                Action = "Create",
                EntityType = "Charge",
                EntityId = "CH-001",
                Description = "Создано первое начисление."
            },
            new FinancialAuditLog
            {
                CreatedAtUtc = new DateTime(2026, 8, 11, 9, 30, 0, DateTimeKind.Utc),
                UserId = "admin-2",
                UserName = "chief.accountant",
                Action = "Create",
                EntityType = "Charge",
                EntityId = "CH-042",
                Description = "Создано повторное начисление."
            },
            new FinancialAuditLog
            {
                CreatedAtUtc = new DateTime(2026, 8, 12, 10, 45, 0, DateTimeKind.Utc),
                UserId = "admin-3",
                UserName = "administrator",
                Action = "Cancel",
                EntityType = "Expense",
                EntityId = "EX-001",
                Description = "Отменён расход."
            });

        await dbContext.SaveChangesAsync();

        var model = new AuditLogIndexModel(dbContext)
        {
            DateFrom = new DateOnly(2026, 8, 10),
            DateTo = new DateOnly(2026, 8, 11),
            User = "chief",
            EntityType = "Charge",
            Action = "Create",
            Search = "CH-0"
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(2, model.TotalCount);
        Assert.Equal(1, model.TotalPages);
        Assert.Equal(2, model.Entries.Count);
        Assert.Equal("CH-042", model.Entries[0].EntityId);
        Assert.Equal("CH-001", model.Entries[1].EntityId);
        Assert.All(model.Entries, item => Assert.Equal("Charge", item.EntityType));
    }

    [Fact]
    public async Task OnGetAsync_PaginatesOnServerAfterFiltering()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var entries = Enumerable.Range(1, 55)
            .Select(index => new FinancialAuditLog
            {
                CreatedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index),
                UserId = "admin",
                UserName = "administrator",
                Action = index <= 52 ? "Create" : "Cancel",
                EntityType = "Charge",
                EntityId = $"CH-{index:000}",
                Description = index <= 52 ? "Target entry" : "Other entry"
            })
            .ToList();

        dbContext.FinancialAuditLogs.AddRange(entries);
        await dbContext.SaveChangesAsync();

        var model = new AuditLogIndexModel(dbContext)
        {
            Action = "Create",
            Search = "Target",
            PageNumber = 2
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(52, model.TotalCount);
        Assert.Equal(2, model.TotalPages);
        Assert.Equal(2, model.Entries.Count);
        Assert.Equal("CH-002", model.Entries[0].EntityId);
        Assert.Equal("CH-001", model.Entries[1].EntityId);
        Assert.True(model.HasPreviousPage);
        Assert.False(model.HasNextPage);
    }
}
