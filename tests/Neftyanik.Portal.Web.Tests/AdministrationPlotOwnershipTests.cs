#if WEB_TESTS
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Administration.Plots.Ownerships;
using MemberPlotCreateModel = Neftyanik.Portal.Web.Pages.Administration.Members.Plots.CreateModel;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationPlotOwnershipTests
{
    [Fact]
    public async Task OnPostCreateOwnershipAsync_WhenPlotAlreadyHasOwner_ReturnsPageWithValidationError()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Members.AddRange(
            new Member { Id = 1, FullName = "Owner One", IsActive = true },
            new Member { Id = 2, FullName = "Owner Two", IsActive = true });
        dbContext.Plots.Add(new Plot { Id = 10, Number = "P-10", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership
        {
            Id = 100,
            PlotId = 10,
            MemberId = 1,
            ValidFrom = new DateOnly(2020, 1, 1),
            IsPrimaryContact = true
        });
        await dbContext.SaveChangesAsync();

        var model = new CreateModel(dbContext)
        {
            Input = new OwnershipInputModel
            {
                MemberId = 2,
                ValidFrom = DateOnly.FromDateTime(DateTime.Today)
            }
        };

        var result = await model.OnPostAsync(10, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.Contains(model.ModelState[string.Empty]!.Errors,
            error => error.ErrorMessage.Contains("уже есть владелец", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, await dbContext.PlotOwnerships.CountAsync());
    }

    [Fact]
    public async Task OnPostRestoreOwnershipAsync_WhenPlotAlreadyHasOwner_ReturnsPageWithValidationError()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Members.AddRange(
            new Member { Id = 1, FullName = "Current Owner", IsActive = true },
            new Member { Id = 2, FullName = "Historical Owner", IsActive = true });
        dbContext.Plots.Add(new Plot { Id = 10, Number = "P-10", IsActive = true });
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
                Id = 101,
                PlotId = 10,
                MemberId = 2,
                ValidFrom = new DateOnly(2019, 1, 1),
                ValidTo = new DateOnly(2019, 12, 31),
                IsPrimaryContact = false
            });
        await dbContext.SaveChangesAsync();

        var model = new RestoreModel(dbContext);

        var result = await model.OnPostAsync(10, 101, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.Contains(model.ModelState[string.Empty]!.Errors,
            error => error.ErrorMessage.Contains("у участка уже есть текущий владелец", StringComparison.OrdinalIgnoreCase));

        var restoredOwnership = await dbContext.PlotOwnerships.AsNoTracking().SingleAsync(item => item.Id == 101);
        Assert.Equal(new DateOnly(2019, 12, 31), restoredOwnership.ValidTo);
    }

    [Fact]
    public async Task OnPostAddPlotToMemberAsync_CreatesOwnershipForSelectedFreePlot()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Members.Add(new Member
        {
            Id = 1,
            FullName = "Plot Assignment Member",
            IsActive = true
        });
        dbContext.Plots.AddRange(
            new Plot { Id = 10, Number = "P-10", IsActive = true },
            new Plot { Id = 11, Number = "P-11", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership
        {
            Id = 100,
            PlotId = 11,
            MemberId = 1,
            ValidFrom = new DateOnly(2020, 1, 1),
            IsPrimaryContact = true
        });
        await dbContext.SaveChangesAsync();

        var model = new MemberPlotCreateModel(dbContext)
        {
            Input = new MemberPlotCreateModel.InputModel
            {
                PlotId = 10,
                ValidFrom = new DateOnly(2026, 1, 1)
            }
        };

        var result = await model.OnPostAsync(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Members/Details", redirect.PageName);

        var ownership = await dbContext.PlotOwnerships
            .AsNoTracking()
            .SingleAsync(item => item.PlotId == 10);

        Assert.Equal(1, ownership.MemberId);
        Assert.Equal(new DateOnly(2026, 1, 1), ownership.ValidFrom);
        Assert.Null(ownership.OwnershipShare);
        Assert.False(ownership.IsPrimaryContact);
    }
}
#endif
