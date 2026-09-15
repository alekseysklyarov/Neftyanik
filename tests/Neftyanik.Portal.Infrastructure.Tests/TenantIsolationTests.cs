using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class TenantIsolationTests : IClassFixture<AssociationDatabaseFixture>
{
    private readonly AssociationDatabaseFixture _database;

    public TenantIsolationTests(AssociationDatabaseFixture database) => _database = database;

    [Fact]
    public async Task CachedModel_UsesEachContextTenantForDbSetsIncludesAndFinance()
    {
        await using var first = _database.CreateContext();
        await using var transaction = await first.Database.BeginTransactionAsync();
        var other = new Association { Name = "Second", Slug = "second" };
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), FirstName = "Shared", LastName = "Identity" };
        first.AddRange(other, user);
        await first.SaveChangesAsync();
        first.AddRange(AssociationFoundationTests.CreateBusinessGraph(first.CurrentAssociationId, user.Id));
        await first.SaveChangesAsync();
        await using var second = await AssociationFoundationTests.ForAssociationAsync(first, other.Id, other.Slug);
        var otherGraph = AssociationFoundationTests.CreateBusinessGraph(other.Id, user.Id);
        otherGraph.OfType<Charge>().Single().Amount = 350m;
        second.AddRange(otherGraph);
        await second.SaveChangesAsync();
        first.ChangeTracker.Clear();
        second.ChangeTracker.Clear();

        Assert.Same(first.Model, second.Model);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var firstPlot = await first.Plots.AsNoTracking().Include(x => x.PlotOwnerships).ThenInclude(x => x.Member).SingleAsync(x => x.Number == "123");
            var secondPlot = await second.Plots.AsNoTracking().Include(x => x.PlotOwnerships).ThenInclude(x => x.Member).SingleAsync(x => x.Number == "123");
            Assert.NotEqual(firstPlot.Id, secondPlot.Id);
            Assert.Equal(first.CurrentAssociationId, firstPlot.AssociationId);
            Assert.Equal(other.Id, secondPlot.AssociationId);
            Assert.All(firstPlot.PlotOwnerships, x => Assert.Equal(first.CurrentAssociationId, x.Member!.AssociationId));
            Assert.All(secondPlot.PlotOwnerships, x => Assert.Equal(other.Id, x.Member!.AssociationId));
            Assert.False(await first.Plots.AnyAsync(x => x.Id == secondPlot.Id));
            Assert.False(await second.Plots.AnyAsync(x => x.Id == firstPlot.Id));
            Assert.Equal(100m, await first.Charges.SumAsync(x => x.Amount));
            Assert.Equal(350m, await second.Charges.SumAsync(x => x.Amount));
            Assert.Equal(2, await first.Associations.CountAsync());
            Assert.Equal(2, await second.Associations.CountAsync());
            Assert.Equal(await first.Users.CountAsync(), await second.Users.CountAsync());
            Assert.Equal(3, await second.Roles.CountAsync());
            var globalUser = await first.Users.AsNoTracking().Include(x => x.Members).SingleAsync(x => x.Id == user.Id);
            Assert.Equal(first.CurrentAssociationId, Assert.Single(globalUser.Members).AssociationId);
        }
    }

    [Fact]
    public async Task UnresolvedScope_ReturnsNoBusinessDataAndRejectsWritesButAllowsGlobals()
    {
        await using var context = _database.CreateUnresolvedContext();
        Assert.NotEmpty(await context.Associations.AsNoTracking().ToListAsync());
        Assert.Empty(await context.ExpenseCategories.AsNoTracking().ToListAsync());
        Assert.Empty(await context.MembershipFeeRates.AsNoTracking().ToListAsync());
        context.Plots.Add(new Plot { Number = "unresolved" });
        await Assert.ThrowsAsync<AssociationIsolationException>(() => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddedEntity_RejectsExplicitForeignOwnershipOrAssociationNavigation(bool useNavigation)
    {
        await using var context = _database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var other = new Association { Name = "Second", Slug = "second" };
        context.Associations.Add(other);
        await context.SaveChangesAsync();
        var plot = new Plot { Number = "forged" };
        if (useNavigation)
        {
            plot.Association = other;
        }
        else
        {
            plot.AssociationId = other.Id;
        }
        context.Plots.Add(plot);
        await Assert.ThrowsAsync<AssociationIsolationException>(() => context.SaveChangesAsync());
        Assert.False(await context.Plots.AsNoTracking().AnyAsync(x => x.Number == "forged"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetachedMutation_RejectsForeignIdEvenWhenClaimedOwnershipMatchesCurrentTenant(bool delete)
    {
        await using var first = _database.CreateContext();
        await using var transaction = await first.Database.BeginTransactionAsync();
        var other = new Association { Name = "Second", Slug = "second" };
        first.Associations.Add(other);
        await first.SaveChangesAsync();
        await using var second = await AssociationFoundationTests.ForAssociationAsync(first, other.Id, other.Slug);
        var victim = new Plot { Number = "10", Address = "original" };
        second.Plots.Add(victim);
        await second.SaveChangesAsync();
        var forged = new Plot { Id = victim.Id, Number = "stolen", AssociationId = first.CurrentAssociationId };
        if (delete)
        {
            first.Remove(forged);
        }
        else
        {
            first.Update(forged);
        }
        await Assert.ThrowsAsync<AssociationIsolationException>(() => first.SaveChangesAsync());
        second.ChangeTracker.Clear();
        var unchanged = await second.Plots.AsNoTracking().SingleAsync(x => x.Id == victim.Id);
        Assert.Equal("10", unchanged.Number);
        Assert.Equal("original", unchanged.Address);
        Assert.Equal(other.Id, unchanged.AssociationId);
    }

    [Fact]
    public async Task ExistingEntity_RejectsAssociationChangeEvenWhenNotPartOfAlternateKey()
    {
        await using var context = _database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var setting = new SystemSetting { Key = "immutable", Value = "original" };
        context.SystemSettings.Add(setting);
        await context.SaveChangesAsync();
        setting.AssociationId = context.CurrentAssociationId + 100;
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        await Assert.ThrowsAsync<AssociationIsolationException>(() => context.SaveChangesAsync());
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
        context.ChangeTracker.Clear();
        Assert.Equal(context.CurrentAssociationId, (await context.SystemSettings.AsNoTracking().SingleAsync(x => x.Id == setting.Id)).AssociationId);
    }

    [Fact]
    public void ResolvedScope_CannotSwitchTenants()
    {
        var context = TestAssociations.Neftyanik;
        Assert.Throws<AssociationIsolationException>(() => context.Resolve(new Association { Id = 2, Slug = "second", Name = "Second" }));
        Assert.Equal(1, context.AssociationId);
    }

    [Fact]
    public void SynchronousSave_CannotBypassTenantEnforcement()
    {
        using var context = _database.CreateContext();
        context.Plots.Add(new Plot { Number = "sync", AssociationId = 999 });
        Assert.Throws<AssociationIsolationException>(() => context.SaveChanges());
    }
}
