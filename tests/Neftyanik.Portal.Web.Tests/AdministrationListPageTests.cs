#if WEB_TESTS
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using MemberIndexModel = Neftyanik.Portal.Web.Pages.Administration.Members.IndexModel;
using PlotIndexModel = Neftyanik.Portal.Web.Pages.Administration.Plots.IndexModel;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationListPageTests
{
    [Fact]
    public async Task OnGetPlotsAsync_DefaultsStatusToAllAndSearchesByOwner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Members.AddRange(
            new Member { Id = 1, FullName = "Анна Владелец", IsActive = true },
            new Member { Id = 2, FullName = "Сергей Поиск", IsActive = true });
        dbContext.Plots.AddRange(
            new Plot { Id = 10, Number = "P-10", IsActive = true },
            new Plot { Id = 20, Number = "P-20", IsActive = false });
        dbContext.PlotOwnerships.AddRange(
            new PlotOwnership
            {
                Id = 100,
                PlotId = 10,
                MemberId = 1,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            },
            new PlotOwnership
            {
                Id = 200,
                PlotId = 20,
                MemberId = 2,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            });

        await dbContext.SaveChangesAsync();

        var model = new PlotIndexModel(dbContext, null!)
        {
            Search = "Сергей"
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal("all", model.Status);
        Assert.Equal(1, model.TotalCount);
        var plot = Assert.Single(model.Plots);
        Assert.Equal(20, plot.Id);
        Assert.Equal("Сергей Поиск", plot.OwnerFullName);
    }

    [Fact]
    public async Task OnGetPlotsAsync_SortsByOwnerDescending()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Members.AddRange(
            new Member { Id = 1, FullName = "Анна Владелец", IsActive = true },
            new Member { Id = 2, FullName = "Борис Владелец", IsActive = true });
        dbContext.Plots.AddRange(
            new Plot { Id = 10, Number = "P-10", IsActive = true },
            new Plot { Id = 20, Number = "P-20", IsActive = true });
        dbContext.PlotOwnerships.AddRange(
            new PlotOwnership
            {
                Id = 100,
                PlotId = 10,
                MemberId = 1,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            },
            new PlotOwnership
            {
                Id = 200,
                PlotId = 20,
                MemberId = 2,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            });

        await dbContext.SaveChangesAsync();

        var model = new PlotIndexModel(dbContext, null!)
        {
            SortBy = "owner",
            SortDirection = "desc"
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(2, model.TotalCount);
        Assert.Equal(20, model.Plots[0].Id);
        Assert.Equal(10, model.Plots[1].Id);
    }

    [Fact]
    public async Task OnGetPlotsAsync_LoadsSelectablePlotsAcrossAllPages()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var member = new Member
        {
            Id = 1,
            FullName = "Общий владелец",
            IsActive = true
        };

        dbContext.Members.Add(member);

        var plots = Enumerable.Range(1, 25)
            .Select(index => new Plot
            {
                Id = index,
                Number = $"P-{index:00}",
                IsActive = true
            })
            .ToList();

        dbContext.Plots.AddRange(plots);
        dbContext.PlotOwnerships.AddRange(
            plots.Select(plot => new PlotOwnership
            {
                PlotId = plot.Id,
                MemberId = member.Id,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            }));

        await dbContext.SaveChangesAsync();

        var model = new PlotIndexModel(dbContext, null!)
        {
            PageNumber = 2
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(25, model.TotalCount);
        Assert.Equal(5, model.Plots.Count);
        Assert.Equal(25, model.SelectablePlots.Count);
        Assert.Equal(21, model.Plots[0].Id);
        Assert.Equal(25, model.SelectablePlots[^1].Id);
    }

    [Fact]
    public async Task OnGetMembersAsync_DefaultsStatusToAllAndSortsByBalanceDescending()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Members.AddRange(
            new Member { Id = 1, FullName = "Активный член", IsActive = true },
            new Member { Id = 2, FullName = "Архивный член", IsActive = false });
        dbContext.Plots.AddRange(
            new Plot { Id = 10, Number = "P-10", IsActive = true },
            new Plot { Id = 20, Number = "P-20", IsActive = true });
        dbContext.ChargeTypes.Add(new ChargeType
        {
            Id = 1,
            Name = "Тестовое начисление",
            IsActive = true,
            DefaultAmount = 100m
        });
        dbContext.PlotOwnerships.AddRange(
            new PlotOwnership
            {
                Id = 100,
                PlotId = 10,
                MemberId = 1,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            },
            new PlotOwnership
            {
                Id = 200,
                PlotId = 20,
                MemberId = 2,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            });
        dbContext.Charges.AddRange(
            new Charge { Id = 1000, PlotId = 10, ChargeTypeId = 1, Amount = 100m, ChargeDate = new DateOnly(2026, 1, 1) },
            new Charge { Id = 2000, PlotId = 20, ChargeTypeId = 1, Amount = 300m, ChargeDate = new DateOnly(2026, 1, 1) });
        dbContext.Payments.Add(new Payment
        {
            Id = 3000,
            PlotId = 10,
            Amount = 50m,
            PaymentDate = new DateOnly(2026, 1, 2)
        });

        await dbContext.SaveChangesAsync();

        var model = new MemberIndexModel(dbContext)
        {
            SortBy = "balance",
            SortDirection = "desc"
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal("all", model.Status);
        Assert.Equal(2, model.TotalCount);
        Assert.Equal(2, model.Members.Count);
        Assert.Equal(2, model.Members[0].Id);
        Assert.Equal(300m, model.Members[0].Balance);
        Assert.Equal(1, model.Members[1].Id);
        Assert.Equal(50m, model.Members[1].Balance);
    }

    [Fact]
    public async Task OnGetMembersAsync_SortsByElectricityDisconnectedFlag()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Members.AddRange(
            new Member { Id = 1, FullName = "Подключённый член", IsActive = true, IsElectricityDisconnected = false },
            new Member { Id = 2, FullName = "Отключённый член", IsActive = true, IsElectricityDisconnected = true });

        await dbContext.SaveChangesAsync();

        var model = new MemberIndexModel(dbContext)
        {
            SortBy = "electricity",
            SortDirection = "desc"
        };

        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(2, model.TotalCount);
        Assert.Equal(2, model.Members[0].Id);
        Assert.True(model.Members[0].IsElectricityDisconnected);
        Assert.Equal(1, model.Members[1].Id);
        Assert.False(model.Members[1].IsElectricityDisconnected);
    }
}
#endif
