using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class AssociationMigrationTests
{
    private const string PreviousMigration = "20260901120211_AddPaymentBalanceSnapshots";
    private const string FoundationMigration = "20260915193703_AddAssociationFoundation";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migration_PreservesEveryExistingColumnAndBackfillsAllTenantTables(bool useIdempotentScript)
    {
        await using var context = new ApplicationDbContext(AssociationDatabaseFixture.CreateOptions());
        try
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            await context.Database.OpenConnectionAsync();
            var user = new ApplicationUser { Id = "legacy-user", UserName = "legacy-user", NormalizedUserName = "LEGACY-USER", FirstName = "Legacy", LastName = "Member" };
            await InsertLegacyEntityAsync(context, user);
            var graph = AssociationFoundationTests.CreateBusinessGraph(0, user.Id);
            foreach (var entity in graph)
            {
                await InsertLegacyEntityAsync(context, entity);
            }
            var plot = graph.OfType<Plot>().Single();
            var meter = graph.OfType<MemberElectricityMeter>().Single();
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Plots] SET [MemberElectricityMeterId] = {meter.Id} WHERE [Id] = {plot.Id}");
            await context.Database.ExecuteSqlRawAsync("UPDATE [ExpenseCategories] SET [Name] = N'Customized production category', [Description] = N'Must survive', [IsActive] = 0 WHERE [Id] = 1");
            await context.Database.ExecuteSqlRawAsync("UPDATE [MembershipFeeRates] SET [AmountPerPlot] = 777.77 WHERE [Id] = 1");

            var before = new Dictionary<string, (string Columns, string OrderBy, string Json)>();
            foreach (var entity in context.Model.GetEntityTypes().Where(x => x.ClrType != typeof(Association)
                && x.ClrType != typeof(AssociationUserMembership) && x.ClrType != typeof(AssociationLoginEvent)))
            {
                var table = entity.GetTableName()!;
                var columns = string.Join(",", entity.GetProperties().Where(x => x.Name != "AssociationId").Select(x => $"[{x.GetColumnName()}]"));
                var orderBy = string.Join(",", entity.FindPrimaryKey()!.Properties.Select(x => $"[{x.GetColumnName()}]"));
                before.Add(table, (columns, orderBy, await ReadJsonAsync(context, table, columns, orderBy)));
            }

            if (useIdempotentScript)
            {
                var script = migrator.GenerateScript(PreviousMigration, FoundationMigration, MigrationsSqlGenerationOptions.Idempotent);
                await ExecuteScriptAsync(context, script);
                await ExecuteScriptAsync(context, script);
            }
            else
            {
                await migrator.MigrateAsync(FoundationMigration);
            }
            await migrator.MigrateAsync(FoundationMigration);

            foreach (var (table, snapshot) in before)
            {
                Assert.Equal(snapshot.Json, await ReadJsonAsync(context, table, snapshot.Columns, snapshot.OrderBy));
            }
            var initial = await context.Associations.AsNoTracking().SingleAsync();
            Assert.Equal("Нефтяник", initial.Name);
            Assert.Equal("neftyanik", initial.Slug);
            Assert.True(initial.IsActive);
            foreach (var entity in context.Model.GetEntityTypes().Where(x => typeof(IAssociationOwned).IsAssignableFrom(x.ClrType)
                && x.ClrType != typeof(AssociationUserMembership) && x.ClrType != typeof(AssociationLoginEvent)))
            {
                var table = entity.GetTableName()!;
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM [{table}] WHERE [AssociationId] IS NULL OR [AssociationId] <> @associationId";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@associationId";
                parameter.Value = initial.Id;
                command.Parameters.Add(parameter);
                Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
                Assert.NotEqual("[]", before[table].Json);
            }
            await AssertConstraintsTrustedAsync(context);

            // The old schema is recoverable while the database still represents one association.
            await migrator.MigrateAsync(PreviousMigration);
            foreach (var (table, snapshot) in before)
            {
                Assert.Equal(snapshot.Json, await ReadJsonAsync(context, table, snapshot.Columns, snapshot.OrderBy));
            }
            await migrator.MigrateAsync(FoundationMigration);
            var other = new Association { Name = "Other", Slug = "other" };
            context.Associations.Add(other);
            await context.SaveChangesAsync();
            Assert.True(other.Id > initial.Id);
            var failure = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => migrator.MigrateAsync(PreviousMigration));
            Assert.Equal(51000, failure.Number);
            Assert.Equal(2, await context.Associations.CountAsync());
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static async Task InsertLegacyEntityAsync(ApplicationDbContext context, object entity)
    {
        // Use current scalar mappings but omit the new tenant column. This writes through SQL,
        // not the new EF save pipeline, against the actual previous production schema.
        var entityType = context.Model.FindEntityType(entity.GetType())!;
        foreach (var foreignKey in entityType.GetForeignKeys())
        {
            var principal = foreignKey.DependentToPrincipal?.PropertyInfo?.GetValue(entity);
            if (principal is null)
            {
                continue;
            }
            for (var index = 0; index < foreignKey.Properties.Count; index++)
            {
                var property = foreignKey.Properties[index];
                if (property.Name != "AssociationId")
                {
                    property.PropertyInfo!.SetValue(entity, foreignKey.PrincipalKey.Properties[index].PropertyInfo!.GetValue(principal));
                }
            }
        }
        var key = entityType.FindPrimaryKey()!.Properties.Single();
        var generatedKey = key.ValueGenerated == ValueGenerated.OnAdd && key.ClrType != typeof(string);
        var properties = entityType.GetProperties().Where(x => x.Name != "AssociationId" && (!generatedKey || x != key)).ToArray();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        var columns = string.Join(",", properties.Select(x => $"[{x.GetColumnName()}]"));
        var values = string.Join(",", properties.Select((_, index) => $"@p{index}"));
        command.CommandText = $"INSERT INTO [{entityType.GetTableName()}] ({columns}) OUTPUT INSERTED.[{key.GetColumnName()}] VALUES ({values})";
        for (var index = 0; index < properties.Length; index++)
        {
            var property = properties[index];
            command.Parameters.Add(property.GetRelationalTypeMapping().CreateParameter(command, $"@p{index}", property.PropertyInfo!.GetValue(entity)));
        }
        var id = await command.ExecuteScalarAsync();
        key.PropertyInfo!.SetValue(entity, Convert.ChangeType(id, key.ClrType));
    }

    internal static async Task<string> ReadJsonAsync(ApplicationDbContext context, string table, string columns, string orderBy)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT {columns} FROM [{table}] ORDER BY {orderBy} FOR JSON PATH, INCLUDE_NULL_VALUES";
        await using var reader = await command.ExecuteReaderAsync();
        var json = new System.Text.StringBuilder();
        while (await reader.ReadAsync())
        {
            json.Append(reader.GetString(0));
        }
        return json.ToString();
    }

    internal static async Task ExecuteScriptAsync(ApplicationDbContext context, string script)
    {
        foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await context.Database.ExecuteSqlRawAsync(batch);
            }
        }
    }

    private static async Task AssertConstraintsTrustedAsync(ApplicationDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1";
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
        command.CommandText = "SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE c.name = 'AssociationId' AND (c.is_nullable = 1 OR c.default_object_id <> 0)";
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }
}
