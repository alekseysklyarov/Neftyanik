using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests.Data.Queries;

public class PlotFinanceQueriesTests
{
    [Fact]
    public async Task WhereCurrentForUser_ReturnsOnlyCurrentOwnershipsForLinkedUser()
    {
        await using var context = CreateContext();
        var currentDate = new DateOnly(2026, 1, 1);

        var memberA = new Member { Id = 1, FullName = "Member A", ApplicationUserId = "member-a" };
        var memberB = new Member { Id = 2, FullName = "Member B", ApplicationUserId = "member-b" };
        var plotA = new Plot { Id = 10, Number = "10" };
        var plotB = new Plot { Id = 20, Number = "20" };

        context.Members.AddRange(memberA, memberB);
        context.Plots.AddRange(plotA, plotB);
        context.PlotOwnerships.AddRange(
            new PlotOwnership { Id = 1, MemberId = memberA.Id, Member = memberA, PlotId = plotA.Id, Plot = plotA },
            new PlotOwnership { Id = 2, MemberId = memberA.Id, Member = memberA, PlotId = plotB.Id, Plot = plotB, ValidTo = currentDate.AddDays(-1) },
            new PlotOwnership { Id = 3, MemberId = memberB.Id, Member = memberB, PlotId = plotB.Id, Plot = plotB });

        await context.SaveChangesAsync();

        var accessiblePlotIds = await context.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForUser("member-a", currentDate)
            .Select(ownership => ownership.PlotId)
            .ToListAsync();

        Assert.Single(accessiblePlotIds);
        Assert.Contains(plotA.Id, accessiblePlotIds);
        Assert.DoesNotContain(plotB.Id, accessiblePlotIds);
    }

    [Fact]
    public async Task SelectFinanceSummary_ExcludesCancelledChargesAndPaymentsFromBalance()
    {
        await using var context = CreateContext();

        var plot = new Plot { Id = 15, Number = "15" };
        context.Plots.Add(plot);
        context.Charges.AddRange(
            new Charge { Id = 1, PlotId = plot.Id, Plot = plot, ChargeTypeId = 1, Amount = 100m, ChargeDate = new DateOnly(2026, 1, 10) },
            new Charge { Id = 2, PlotId = plot.Id, Plot = plot, ChargeTypeId = 1, Amount = 40m, ChargeDate = new DateOnly(2026, 1, 11), CancelledAtUtc = DateTime.UtcNow });
        context.Payments.AddRange(
            new Payment { Id = 1, PlotId = plot.Id, Plot = plot, Amount = 25m, PaymentDate = new DateOnly(2026, 1, 12), PaymentMethod = PaymentMethod.Cash },
            new Payment { Id = 2, PlotId = plot.Id, Plot = plot, Amount = 10m, PaymentDate = new DateOnly(2026, 1, 13), PaymentMethod = PaymentMethod.Card, CancelledAtUtc = DateTime.UtcNow });

        await context.SaveChangesAsync();

        var summary = await context.Plots
            .AsNoTracking()
            .Where(plotItem => plotItem.Id == plot.Id)
            .SelectFinanceSummary()
            .SingleAsync();

        Assert.Equal(100m, summary.ActiveChargesTotal);
        Assert.Equal(25m, summary.ActivePaymentsTotal);
        Assert.Equal(75m, summary.Balance);
    }

    [Fact]
    public async Task SelectFinanceSummary_ReturnsZeroTotalsForPlotWithoutTransactions()
    {
        await using var context = CreateContext();

        context.Plots.Add(new Plot { Id = 30, Number = "30" });
        await context.SaveChangesAsync();

        var summary = await context.Plots
            .AsNoTracking()
            .Where(plot => plot.Id == 30)
            .SelectFinanceSummary()
            .SingleAsync();

        Assert.Equal(0m, summary.ActiveChargesTotal);
        Assert.Equal(0m, summary.ActivePaymentsTotal);
        Assert.Equal(0m, summary.Balance);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
