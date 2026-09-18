using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class AssociationMembershipTests : IClassFixture<AssociationDatabaseFixture>
{
    private readonly AssociationDatabaseFixture _database;
    public AssociationMembershipTests(AssociationDatabaseFixture database) => _database = database;

    [Fact]
    public async Task Memberships_UseCurrentAssociationAndNeverGlobalIdentityRoles()
    {
        await using var database = _database.CreateContext();
        var user = new ApplicationUser { FirstName = "Test", LastName = "User" };
        var second = new Association { Slug = "membership-" + Guid.NewGuid().ToString("N"), Name = "Second" };
        database.AddRange(user, second);
        database.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUser = user, Role = RoleNames.Member });
        await database.SaveChangesAsync();
        Assert.Equal(new[] { RoleNames.Member }, await new AssociationMembershipService(database).GetRolesAsync(user.Id));
        await using var unresolved = _database.CreateUnresolvedContext();
        Assert.Empty(await new AssociationMembershipService(unresolved).GetRolesAsync(user.Id));
        Assert.Empty(await unresolved.AssociationUserMemberships.ToListAsync());
        var membership = await database.AssociationUserMemberships.SingleAsync(x => x.ApplicationUserId == user.Id);
        membership.IsActive = false;
        await database.SaveChangesAsync();
        Assert.Empty(await new AssociationMembershipService(database).GetRolesAsync(user.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetRolesAsync_UsesOnlyResolvedAssociationEvenWithoutQueryFilter(bool removeMembershipFilter)
    {
        await using var associationA = _database.CreateContext();
        var user = new ApplicationUser { FirstName = "Shared", LastName = "User" };
        var second = new Association { Slug = "role-scope-" + Guid.NewGuid().ToString("N"), Name = "Association B" };
        associationA.AddRange(user, second);
        associationA.AssociationUserMemberships.Add(new AssociationUserMembership
        {
            ApplicationUser = user, Role = RoleNames.Administrator
        });
        await associationA.SaveChangesAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(associationA.Database.GetConnectionString()).Options;
        var contextB = TestAssociations.Resolved(second.Id, second.Slug);
        await using ApplicationDbContext associationB = removeMembershipFilter
            ? new UnfilteredMembershipContext(options, contextB)
            : new ApplicationDbContext(options, contextB);
        associationB.AssociationUserMemberships.Add(new AssociationUserMembership
        {
            ApplicationUserId = user.Id, Role = RoleNames.Member
        });
        await associationB.SaveChangesAsync();

        Assert.NotNull(associationA.Model.FindEntityType(typeof(AssociationUserMembership))!.GetQueryFilter());
        Assert.Equal(removeMembershipFilter ? 2 : 1,
            await associationB.AssociationUserMemberships.CountAsync(x => x.ApplicationUserId == user.Id));
        if (removeMembershipFilter)
        {
            Assert.Null(associationB.Model.FindEntityType(typeof(AssociationUserMembership))!.GetQueryFilter());
        }

        Assert.Equal(new[] { RoleNames.Administrator }, await new AssociationMembershipService(associationA).GetRolesAsync(user.Id));
        var rolesInB = await new AssociationMembershipService(associationB).GetRolesAsync(user.Id);
        Assert.Equal(new[] { RoleNames.Member }, rolesInB);
        Assert.DoesNotContain(RoleNames.Administrator, rolesInB);

        await using ApplicationDbContext unresolved = removeMembershipFilter
            ? new UnfilteredMembershipContext(options, new AssociationContext())
            : new ApplicationDbContext(options);
        Assert.Empty(await new AssociationMembershipService(unresolved).GetRolesAsync(user.Id));
    }

    [Fact]
    public async Task Memberships_RejectDuplicateRoleButAllowMultipleRoles()
    {
        await using var database = _database.CreateContext();
        var user = new ApplicationUser { FirstName = "Test", LastName = "User" };
        database.AssociationUserMemberships.AddRange(
            new AssociationUserMembership { ApplicationUser = user, Role = RoleNames.Member },
            new AssociationUserMembership { ApplicationUser = user, Role = RoleNames.Accountant });
        await database.SaveChangesAsync();
        Assert.Equal(2, await database.AssociationUserMemberships.CountAsync(x => x.ApplicationUserId == user.Id));
        database.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = user.Id, Role = RoleNames.Member });
        await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync());
    }

    [Theory]
    [InlineData("create")]
    [InlineData("modify")]
    [InlineData("delete")]
    public async Task Memberships_RejectForgedAssociationAndForeignStub(string operation)
    {
        await using var seed = _database.CreateContext();
        var user = new ApplicationUser { FirstName = "Test", LastName = "User" };
        var second = new Association { Slug = "forgery-" + Guid.NewGuid().ToString("N"), Name = "Second" };
        seed.AddRange(user, second);
        await seed.SaveChangesAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(seed.Database.GetConnectionString()).Options;
        await using var foreign = new ApplicationDbContext(options, TestAssociations.Resolved(second.Id, second.Slug));
        var membership = new AssociationUserMembership { ApplicationUserId = user.Id, Role = RoleNames.Member };
        foreign.AssociationUserMemberships.Add(membership);
        await foreign.SaveChangesAsync();
        await using var database = _database.CreateContext();
        if (operation == "create")
        {
            database.AssociationUserMemberships.Add(new AssociationUserMembership { AssociationId = second.Id, ApplicationUserId = user.Id, Role = RoleNames.Administrator });
        }
        else
        {
            var stub = new AssociationUserMembership { Id = membership.Id, AssociationId = database.CurrentAssociationId, ApplicationUserId = user.Id, Role = RoleNames.Administrator };
            if (operation == "modify") database.Update(stub); else database.Remove(stub);
        }
        await Assert.ThrowsAsync<AssociationIsolationException>(() => database.SaveChangesAsync());
        await foreign.Entry(membership).ReloadAsync();
        Assert.True(membership.IsActive);
        Assert.Equal(RoleNames.Member, membership.Role);
        Assert.Equal(second.Id, membership.AssociationId);
    }

    private sealed class UnfilteredMembershipContext(DbContextOptions<ApplicationDbContext> options, IAssociationContext associationContext)
        : ApplicationDbContext(options, associationContext)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<AssociationUserMembership>().HasQueryFilter(null);
        }
    }
}
