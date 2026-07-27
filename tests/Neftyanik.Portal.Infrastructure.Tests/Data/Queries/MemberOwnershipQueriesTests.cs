using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests.Data.Queries;

public class MemberOwnershipQueriesTests
{
    [Fact]
    public async Task WhereCurrentForMember_WhenOwnershipBeginsToday_ReturnsOwnership()
    {
        await using var context = CreateContext();
        var currentDate = new DateOnly(2026, 5, 10);

        context.PlotOwnerships.Add(new PlotOwnership
        {
            Id = 1,
            MemberId = 1,
            PlotId = 10,
            ValidFrom = currentDate
        });

        await context.SaveChangesAsync();

        var ownershipIds = await context.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(1, currentDate)
            .Select(ownership => ownership.Id)
            .ToListAsync();

        Assert.Single(ownershipIds);
        Assert.Contains(1, ownershipIds);
    }

    [Fact]
    public async Task WhereCurrentForMember_WhenOwnershipBeginsTomorrow_DoesNotReturnOwnership()
    {
        await using var context = CreateContext();
        var currentDate = new DateOnly(2026, 5, 10);

        context.PlotOwnerships.Add(new PlotOwnership
        {
            Id = 1,
            MemberId = 1,
            PlotId = 10,
            ValidFrom = currentDate.AddDays(1)
        });

        await context.SaveChangesAsync();

        var ownershipIds = await context.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(1, currentDate)
            .Select(ownership => ownership.Id)
            .ToListAsync();

        Assert.Empty(ownershipIds);
    }

    [Fact]
    public async Task WhereCurrentForMember_WhenOwnershipEndsToday_ReturnsOwnership()
    {
        await using var context = CreateContext();
        var currentDate = new DateOnly(2026, 5, 10);

        context.PlotOwnerships.Add(new PlotOwnership
        {
            Id = 1,
            MemberId = 1,
            PlotId = 10,
            ValidFrom = currentDate.AddDays(-10),
            ValidTo = currentDate
        });

        await context.SaveChangesAsync();

        var ownershipIds = await context.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(1, currentDate)
            .Select(ownership => ownership.Id)
            .ToListAsync();

        Assert.Single(ownershipIds);
        Assert.Contains(1, ownershipIds);
    }

    [Fact]
    public async Task WhereCurrentForMember_WhenOwnershipEndedYesterday_DoesNotReturnOwnership()
    {
        await using var context = CreateContext();
        var currentDate = new DateOnly(2026, 5, 10);

        context.PlotOwnerships.Add(new PlotOwnership
        {
            Id = 1,
            MemberId = 1,
            PlotId = 10,
            ValidFrom = currentDate.AddDays(-10),
            ValidTo = currentDate.AddDays(-1)
        });

        await context.SaveChangesAsync();

        var ownershipIds = await context.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(1, currentDate)
            .Select(ownership => ownership.Id)
            .ToListAsync();

        Assert.Empty(ownershipIds);
    }

    [Fact]
    public async Task WhereCurrentForMember_WhenOwnershipHasNoEndDateAndPastStart_ReturnsOwnership()
    {
        await using var context = CreateContext();
        var currentDate = new DateOnly(2026, 5, 10);

        context.PlotOwnerships.Add(new PlotOwnership
        {
            Id = 1,
            MemberId = 1,
            PlotId = 10,
            ValidFrom = currentDate.AddDays(-10)
        });

        await context.SaveChangesAsync();

        var ownershipIds = await context.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(1, currentDate)
            .Select(ownership => ownership.Id)
            .ToListAsync();

        Assert.Single(ownershipIds);
        Assert.Contains(1, ownershipIds);
    }

    [Fact]
    public async Task WhereCurrentForMember_WhenValidFromIsNull_TreatsOwnershipAsCurrent()
    {
        await using var context = CreateContext();
        var currentDate = new DateOnly(2026, 5, 10);

        context.PlotOwnerships.Add(new PlotOwnership
        {
            Id = 1,
            MemberId = 1,
            PlotId = 10,
            ValidFrom = null,
            ValidTo = null
        });

        await context.SaveChangesAsync();

        var ownershipIds = await context.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(1, currentDate)
            .Select(ownership => ownership.Id)
            .ToListAsync();

        Assert.Single(ownershipIds);
        Assert.Contains(1, ownershipIds);
    }

    [Fact]
    public async Task WhereCurrentForMember_DoesNotReturnAnotherMembersOwnership()
    {
        await using var context = CreateContext();
        var currentDate = new DateOnly(2026, 5, 10);

        context.PlotOwnerships.AddRange(
            new PlotOwnership
            {
                Id = 1,
                MemberId = 1,
                PlotId = 10,
                ValidFrom = currentDate.AddDays(-5)
            },
            new PlotOwnership
            {
                Id = 2,
                MemberId = 2,
                PlotId = 20,
                ValidFrom = currentDate.AddDays(-5)
            });

        await context.SaveChangesAsync();

        var ownershipIds = await context.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(1, currentDate)
            .Select(ownership => ownership.Id)
            .ToListAsync();

        Assert.Single(ownershipIds);
        Assert.Contains(1, ownershipIds);
        Assert.DoesNotContain(2, ownershipIds);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
