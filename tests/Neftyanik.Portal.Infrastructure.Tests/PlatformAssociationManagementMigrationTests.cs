using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class PlatformAssociationManagementMigrationTests
{
    private const string Previous = "20260920163502_AddPlatformPasswordRecoveryAudit";
    private const string Current = "20260922175600_AddPlatformAssociationManagement";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration_PreservesEveryExistingColumnAndAllowsNullContacts(bool script)
    {
        var options = AssociationDatabaseFixture.CreateOptions();
        var tenant = new AssociationContext();
        await using var database = PrePlatformManagementDbContext.Create(options, tenant);
        try
        {
            var migrations = database.GetService<IMigrationsAssembly>().Migrations;
            Assert.Contains(Previous, migrations.Keys);
            Assert.Contains(Current, migrations.Keys);
            var migrator = database.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);
            var association = await database.Associations.SingleAsync(x => x.Slug == "neftyanik");
            association.Name = "Existing customized name";
            tenant.Resolve(association);
            database.Users.Add(new ApplicationUser { Id = "preserve-user", UserName = "preserve-user", FirstName = "Test", LastName = "Member" });
            database.AddRange(AssociationFoundationTests.CreateBusinessGraph(association.Id, "preserve-user"));
            database.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = "preserve-user", Role = RoleNames.Administrator, IsActive = false });
            database.PlatformBootstrapStates.Add(new PlatformBootstrapState { Id = 1, Reason = "Preserve existing state", ConsumedAtUtc = DateTimeOffset.UtcNow });
            await database.SaveChangesAsync();
            await database.Database.OpenConnectionAsync();
            var snapshots = new Dictionary<string, (string Columns, string Order, string Json)>();
            foreach (var entity in database.Model.GetEntityTypes())
            {
                var table = entity.GetTableName()!;
                var columns = string.Join(",", entity.GetProperties().Select(x => $"[{x.GetColumnName()}]"));
                var order = string.Join(",", entity.FindPrimaryKey()!.Properties.Select(x => $"[{x.GetColumnName()}]"));
                snapshots.Add(table, (columns, order, await AssociationMigrationTests.ReadJsonAsync(database, table, columns, order)));
            }
            if (script)
            {
                var sql = migrator.GenerateScript(Previous, Current, MigrationsSqlGenerationOptions.Idempotent);
                await AssociationMigrationTests.ExecuteScriptAsync(database, sql);
                await AssociationMigrationTests.ExecuteScriptAsync(database, sql);
            }
            else
            {
                await migrator.MigrateAsync(Current);
            }
            foreach (var (table, snapshot) in snapshots)
            {
                Assert.Equal(snapshot.Json, await AssociationMigrationTests.ReadJsonAsync(database, table, snapshot.Columns, snapshot.Order));
            }
            await using var current = new ApplicationDbContext(options);
            var preserved = await current.Associations.AsNoTracking().SingleAsync(x => x.Id == association.Id);
            Assert.Equal("Existing customized name", preserved.Name);
            Assert.Equal("neftyanik", preserved.Slug);
            Assert.True(preserved.IsActive);
            Assert.Null(preserved.ContactEmail);
            Assert.Null(preserved.ContactPhone);
            Assert.Null(preserved.PostalAddress);
            Assert.Equal(Guid.Empty, preserved.Revision);
            Assert.Empty(await current.PlatformAuditLogs.ToListAsync());
        }
        finally
        {
            await database.Database.CloseConnectionAsync();
            await database.Database.EnsureDeletedAsync();
        }
    }
}
