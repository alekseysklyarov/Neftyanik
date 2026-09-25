using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Services;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class PlatformAssociationReaderTests(AssociationDatabaseFixture fixture) : IClassFixture<AssociationDatabaseFixture>
{
    [Fact]
    public async Task ReadAsync_ProjectsCrossTenantMetadataOnSqlServerWithoutResolvingOrTrackingTenantData()
    {
        await using var tenant = fixture.CreateContext();
        await using var transaction = await tenant.Database.BeginTransactionAsync();
        var association = await tenant.Associations.SingleAsync(x => x.Id == tenant.CurrentAssociationId);
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = "assigned-admin", FirstName = "Private", LastName = "Private", IsActive = false };
        tenant.Users.Add(user);
        tenant.Members.AddRange(new Member { FullName = "With account", ApplicationUserId = user.Id }, new Member { FullName = "Without account", IsActive = false });
        tenant.AssociationUserMemberships.AddRange(
            new AssociationUserMembership { ApplicationUserId = user.Id, Role = RoleNames.Administrator },
            new AssociationUserMembership { ApplicationUserId = user.Id, Role = RoleNames.Member },
            new AssociationUserMembership { ApplicationUserId = user.Id, Role = RoleNames.Accountant });
        association.IsActive = false;
        tenant.Associations.Add(new Association { Name = "Empty association", Slug = "empty" });
        await tenant.SaveChangesAsync();

        var options = new DbContextOptionsBuilder<Neftyanik.Portal.Infrastructure.Data.ApplicationDbContext>()
            .UseSqlServer(tenant.Database.GetDbConnection()).Options;
        await using var database = new Neftyanik.Portal.Infrastructure.Data.ApplicationDbContext(options);
        await database.Database.UseTransactionAsync(Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(transaction));
        var access = new ReadAccess();
        var reader = new PlatformAssociationReader(database, access, TimeProvider.System);
        var all = await reader.GetPageAsync();
        Assert.Equal(2, all.TotalCount);
        var row = all.Items.Single(x => x.Id == association.Id);
        Assert.False(row.IsActive);
        Assert.Equal(2, row.MemberCount);
        Assert.Equal(1, row.AdministratorCount);
        var filtered = await reader.GetPageAsync("neftyanik", false, int.MaxValue);
        Assert.Single(filtered.Items);
        Assert.Equal(1, filtered.PageNumber);
        Assert.Single((await reader.GetPageAsync(isActive: true)).Items);
        var details = await reader.GetDetailsAsync(association.Id);
        Assert.NotNull(details);
        Assert.Equal(2, details.MemberCount);
        var administrator = Assert.Single(details.Administrators);
        Assert.Equal("assigned-admin", administrator.UserName);
        Assert.True(administrator.IsAssignmentActive);
        Assert.False(administrator.IsAccountActive);
        Assert.Null(await reader.GetDetailsAsync(int.MaxValue));
        Assert.Equal(5, access.Calls);
        Assert.False(database.IsAssociationResolved);
        Assert.Empty(database.ChangeTracker.Entries());
        Assert.Empty(await database.Members.ToListAsync());
        Assert.Empty(await database.AssociationUserMemberships.ToListAsync());
        Assert.Empty(await database.Charges.ToListAsync());
    }

    [Fact]
    public async Task ReadAsync_ChecksAccessBeforeQueryingEvenForMissingAssociation()
    {
        await using var database = fixture.CreateUnresolvedContext();
        var access = new ReadAccess { Denied = true };
        var reader = new PlatformAssociationReader(database, access, TimeProvider.System);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetPageAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetDetailsAsync(int.MaxValue));
        Assert.Equal(2, access.Calls);
        Assert.Empty(database.ChangeTracker.Entries());
    }

    private sealed class ReadAccess : IPlatformAssociationReadAccess
    {
        public bool Denied { get; init; }
        public int Calls { get; private set; }

        public Task EnsureAllowedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (Denied) throw new UnauthorizedAccessException();
            return Task.CompletedTask;
        }
    }
}
