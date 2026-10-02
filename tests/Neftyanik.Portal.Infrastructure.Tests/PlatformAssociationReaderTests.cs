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
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetHistoryAsync(int.MaxValue));
        Assert.Equal(3, access.Calls);
        Assert.Empty(database.ChangeTracker.Entries());
    }

    [Fact]
    public async Task History_FiltersSortsAndPaginatesOnSqlServerWithoutTrackingOrWriting()
    {
        await using var seed = fixture.CreateContext();
        await using var transaction = await seed.Database.BeginTransactionAsync();
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = "history-operator" };
        var other = new Association { Name = "Other history", Slug = "other-history" };
        seed.AddRange(user, other);
        await seed.SaveChangesAsync();
        var date = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var entries = Enumerable.Range(0, 43).Select(i => new PlatformAuditLog
        {
            AssociationId = seed.CurrentAssociationId, OperatorUserId = user.Id,
            OccurredAtUtc = date.AddMinutes(i % 3), Action = PlatformAuditActions.AssociationEdited,
            OldValuesJson = "{\"Name\":\"Before\"}", NewValuesJson = "{\"Name\":\"After\"}"
        }).ToArray();
        seed.PlatformAuditLogs.AddRange(entries);
        seed.PlatformAuditLogs.Add(new PlatformAuditLog
        {
            AssociationId = other.Id, OperatorUserId = user.Id, OccurredAtUtc = date.AddYears(1),
            Action = PlatformAuditActions.AssociationCreated, OldValuesJson = "{}", NewValuesJson = "{\"Name\":\"Foreign\"}"
        });
        await seed.SaveChangesAsync();
        var before = await SnapshotAsync(seed);
        var options = new DbContextOptionsBuilder<Neftyanik.Portal.Infrastructure.Data.ApplicationDbContext>()
            .UseSqlServer(seed.Database.GetDbConnection()).Options;
        await using var database = new Neftyanik.Portal.Infrastructure.Data.ApplicationDbContext(options);
        await database.Database.UseTransactionAsync(Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(transaction));
        var reader = new PlatformAssociationReader(database, new ReadAccess(), TimeProvider.System);
        var expected = entries.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).Select(x => x.Id).ToArray();
        var first = await reader.GetHistoryAsync(seed.CurrentAssociationId, -1);
        var second = await reader.GetHistoryAsync(seed.CurrentAssociationId, 2);
        var last = await reader.GetHistoryAsync(seed.CurrentAssociationId, int.MaxValue);
        Assert.Equal(43, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(1, first.PageNumber);
        Assert.Equal(3, last.PageNumber);
        Assert.Equal(expected.Take(20), first.Items.Select(x => x.Id));
        Assert.Equal(expected.Skip(20).Take(20), second.Items.Select(x => x.Id));
        Assert.Equal(expected.Skip(40), last.Items.Select(x => x.Id));
        Assert.All(first.Items, x => Assert.Equal("history-operator", x.OperatorUserName));
        Assert.Equal(first.Items.Select(x => x.Id), (await reader.GetHistoryAsync(seed.CurrentAssociationId)).Items.Select(x => x.Id));
        var empty = await reader.GetHistoryAsync(int.MaxValue);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalCount);
        Assert.Equal(1, empty.TotalPages);
        Assert.Empty(database.ChangeTracker.Entries());
        Assert.False(database.IsAssociationResolved);
        Assert.Equal(before, await SnapshotAsync(seed));
    }

    private static async Task<string> SnapshotAsync(Neftyanik.Portal.Infrastructure.Data.ApplicationDbContext database) =>
        System.Text.Json.JsonSerializer.Serialize(await database.PlatformAuditLogs.AsNoTracking().OrderBy(x => x.Id).ToListAsync());

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
