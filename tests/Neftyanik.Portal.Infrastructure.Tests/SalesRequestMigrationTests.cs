using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class SalesRequestMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upgrade_PreservesLegacyConsentAndBackfillsContactMethodAndPilotStage(bool script)
    {
        const string previous = "20261017064609_AddSalesRequestNotifications";
        const string current = "20261017083126_AddSalesPilotCards";
        await using var db = new ApplicationDbContext(AssociationDatabaseFixture.CreateOptions());
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(previous);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO [DemoRequests] ([ContactName], [AssociationName], [Email], [CreatedAtUtc], [ContactedAtUtc], [ConsentVersion], [NotificationAttemptCount])
                VALUES ('New contact', 'Legacy garden', 'new@example.invalid', '2026-10-17', NULL, 'demo-contact-v1', 0),
                       ('Existing contact', 'Legacy garden', 'old@example.invalid', '2026-10-17', '2026-10-18', 'demo-contact-v1', 2)
                """);
            if (script)
            {
                await db.Database.OpenConnectionAsync();
                var sql = migrator.GenerateScript(previous, current, MigrationsSqlGenerationOptions.Idempotent);
                await AssociationMigrationTests.ExecuteScriptAsync(db, sql);
                await AssociationMigrationTests.ExecuteScriptAsync(db, sql);
            }
            else await migrator.MigrateAsync(current);
            var requests = await db.DemoRequests.OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(2, requests.Count);
            Assert.Equal("new", requests[0].PilotStage);
            Assert.Equal("contacted", requests[1].PilotStage);
            Assert.Equal(2, requests[1].NotificationAttemptCount);
            Assert.Equal("old@example.invalid", requests[1].Email);
            Assert.All(requests, request =>
            {
                Assert.Equal("demo-contact-v1", request.ConsentVersion);
                Assert.Equal("email", request.PreferredContactMethod);
                Assert.Null(request.Phone);
                Assert.Null(request.PilotStartDate);
                Assert.Equal(Guid.Empty, request.PilotRevision);
            });
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
