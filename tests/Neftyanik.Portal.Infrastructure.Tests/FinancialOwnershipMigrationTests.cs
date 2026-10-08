using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Neftyanik.Portal.Domain.Entities;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class FinancialOwnershipMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration_BackfillsHistoricalOwnerWithoutTransferringDebtOrLosingPayments(bool useScript)
    {
        var fixture = new AssociationDatabaseFixture();
        await fixture.InitializeAsync();
        try
        {
            await using var db = fixture.CreateContext();
            var oldOwner = new Member { FullName = "Old owner" };
            var newOwner = new Member { FullName = "New owner" };
            var plot = new Plot { Number = "transfer-test" };
            var unknownPlot = new Plot { Number = "unknown-test" };
            var type = new ChargeType { Name = "Migration charge" };
            db.AddRange(oldOwner, newOwner, plot, unknownPlot, type);
            await db.SaveChangesAsync();
            db.PlotOwnerships.AddRange(new PlotOwnership { MemberId = oldOwner.Id, PlotId = plot.Id, ValidFrom = new(2020, 1, 1), ValidTo = new(2026, 6, 30) },
                new PlotOwnership { MemberId = newOwner.Id, PlotId = plot.Id, ValidFrom = new(2026, 7, 1) });
            var debt = new Charge { MemberId = oldOwner.Id, PlotId = plot.Id, ChargeTypeId = type.Id, Amount = 150, ChargeDate = new(2026, 5, 1) };
            var unknown = new Charge { PlotId = unknownPlot.Id, ChargeTypeId = type.Id, Amount = 45, ChargeDate = new(2026, 5, 1) };
            var payment = new Payment { MemberId = oldOwner.Id, PlotId = plot.Id, Amount = 100, PaymentDate = new(2026, 5, 2) };
            db.AddRange(debt, unknown, payment);
            await db.SaveChangesAsync();
            db.PaymentAllocations.Add(new PaymentAllocation { ChargeId = debt.Id, PaymentId = payment.Id, Amount = 100 });
            await db.SaveChangesAsync();
            var migrations = db.Database.GetMigrations().ToArray();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(migrations[^2]);
            if (useScript)
            {
                await db.Database.OpenConnectionAsync();
                var script = migrator.GenerateScript(migrations[^2], migrations[^1], MigrationsSqlGenerationOptions.Idempotent);
                await AssociationMigrationTests.ExecuteScriptAsync(db, script);
                await AssociationMigrationTests.ExecuteScriptAsync(db, script);
            }
            else await migrator.MigrateAsync();
            db.ChangeTracker.Clear();
            Assert.Equal(oldOwner.Id, (await db.Charges.SingleAsync(c => c.Id == debt.Id)).MemberId);
            Assert.Null((await db.Charges.SingleAsync(c => c.Id == unknown.Id)).MemberId);
            Assert.Equal(150, (await db.Charges.SingleAsync(c => c.Id == debt.Id)).Amount);
            Assert.Equal(100, (await db.Payments.SingleAsync(p => p.Id == payment.Id)).Amount);
            Assert.Equal(100, (await db.PaymentAllocations.SingleAsync(a => a.ChargeId == debt.Id)).Amount);
            Assert.False(await db.Charges.AnyAsync(c => c.MemberId == newOwner.Id));
        }
        finally { await fixture.DisposeAsync(); }
    }
}
