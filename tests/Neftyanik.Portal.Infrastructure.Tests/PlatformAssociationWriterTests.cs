using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class PlatformAssociationWriterTests(AssociationDatabaseFixture fixture) : IClassFixture<AssociationDatabaseFixture>
{
    [Fact]
    public async Task UpdateAsync_RecordsOnlyActualChangesAndRepeatedPostDoesNotAddAudit()
    {
        var (id, actor) = await SeedAsync();
        await using var database = fixture.CreateUnresolvedContext();
        var writer = Writer(database, actor);
        var update = new AssociationMetadataUpdate(" Updated ", " contact@example.invalid ", " +380123 ", " Address ", Guid.Empty);
        Assert.Equal(AssociationWriteOutcome.Saved, (await writer.UpdateAsync(id, update)).Outcome);
        Assert.Equal(AssociationWriteOutcome.Unchanged, (await writer.UpdateAsync(id, update)).Outcome);
        var state = (await writer.GetAsync(id))!;
        Assert.Equal("Updated", state.Name);
        Assert.Equal("contact@example.invalid", state.ContactEmail);
        Assert.NotEqual(Guid.Empty, state.Revision);
        var audit = await database.PlatformAuditLogs.AsNoTracking().SingleAsync(x => x.AssociationId == id);
        Assert.Equal(actor, audit.OperatorUserId);
        Assert.Equal(PlatformAuditActions.AssociationEdited, audit.Action);
        Assert.Equal(TimeSpan.Zero, audit.OccurredAtUtc.Offset);
        using var oldValues = JsonDocument.Parse(audit.OldValuesJson);
        using var newValues = JsonDocument.Parse(audit.NewValuesJson);
        Assert.Equal("Original", oldValues.RootElement.GetProperty("Name").GetString());
        Assert.Equal("Updated", newValues.RootElement.GetProperty("Name").GetString());
        Assert.Equal(4, newValues.RootElement.EnumerateObject().Count());
        Assert.False(newValues.RootElement.TryGetProperty("Slug", out _));
        Assert.False(database.IsAssociationResolved);
        Assert.Empty(database.ChangeTracker.Entries<IAssociationOwned>());
        Assert.Equal(AssociationWriteOutcome.Conflict, (await writer.UpdateAsync(id, update with { Name = "Stale overwrite" })).Outcome);
        Assert.Equal(1, await database.PlatformAuditLogs.CountAsync(x => x.AssociationId == id));
    }

    [Fact]
    public async Task SetActiveAsync_PreservesNeftyanikAndDoesNotAuditNoOp()
    {
        var (_, actor) = await SeedAsync();
        await using var database = fixture.CreateUnresolvedContext();
        var initial = await database.Associations.AsNoTracking().SingleAsync(x => x.Slug == "neftyanik");
        var writer = Writer(database, actor);
        Assert.Equal(AssociationWriteOutcome.ProtectedAssociation, (await writer.SetActiveAsync(initial.Id, false, initial.Revision)).Outcome);
        Assert.Equal(AssociationWriteOutcome.Unchanged, (await writer.SetActiveAsync(initial.Id, true, initial.Revision)).Outcome);
        Assert.True((await database.Associations.AsNoTracking().SingleAsync(x => x.Id == initial.Id)).IsActive);
        Assert.False(await database.PlatformAuditLogs.AnyAsync(x => x.AssociationId == initial.Id));
    }

    [Fact]
    public async Task UpdateAsync_AuditDatabaseFailureRollsBackMetadataAndRevision()
    {
        var (id, actor) = await SeedAsync();
        await using var database = fixture.CreateUnresolvedContext();
        await database.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailPlatformAudit ON PlatformAuditLogs AFTER INSERT AS BEGIN THROW 51090, 'Test audit failure', 1; END");
        try
        {
            var writer = Writer(database, actor);
            Assert.Equal(AssociationWriteOutcome.Failed, (await writer.UpdateAsync(id, new("Must roll back", null, null, null, Guid.Empty))).Outcome);
            var association = await database.Associations.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Equal("Original", association.Name);
            Assert.Equal(Guid.Empty, association.Revision);
            Assert.False(await database.PlatformAuditLogs.AnyAsync(x => x.AssociationId == id));
        }
        finally
        {
            await database.Database.ExecuteSqlRawAsync("DROP TRIGGER FailPlatformAudit");
        }
    }

    [Fact]
    public async Task ConcurrentUpdates_CommitOneChangeAndOneConsistentAudit()
    {
        var (id, actor) = await SeedAsync();
        await using var source = fixture.CreateUnresolvedContext();
        var barrier = new SaveBarrier();
        var commands = new UpdateCommandCapture();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(source.Database.GetConnectionString()).AddInterceptors(barrier, commands).Options;
        await using var first = new ApplicationDbContext(options);
        await using var second = new ApplicationDbContext(options);
        var results = await Task.WhenAll(
            Writer(first, actor).UpdateAsync(id, new("First", null, null, null, Guid.Empty)),
            Writer(second, actor).UpdateAsync(id, new("Second", null, null, null, Guid.Empty)));
        Assert.Single(results.Where(x => x.Outcome == AssociationWriteOutcome.Saved));
        Assert.Single(results.Where(x => x.Outcome == AssociationWriteOutcome.Conflict));
        Assert.Equal(2, commands.Updates.Count);
        Assert.All(commands.Updates, sql => Assert.Matches(
            @"UPDATE \[Associations\][\s\S]*?WHERE \[Id\] = @\w+ AND \[Revision\] = @\w+", sql));
        var association = await source.Associations.AsNoTracking().SingleAsync(x => x.Id == id);
        var audit = await source.PlatformAuditLogs.AsNoTracking().SingleAsync(x => x.AssociationId == id);
        using var oldValues = JsonDocument.Parse(audit.OldValuesJson);
        using var newValues = JsonDocument.Parse(audit.NewValuesJson);
        Assert.Equal("Original", oldValues.RootElement.GetProperty("Name").GetString());
        Assert.Equal(association.Name, newValues.RootElement.GetProperty("Name").GetString());
    }

    [Theory]
    [InlineData("", null, null, null, "Name")]
    [InlineData("Valid", "invalid-email", null, null, "ContactEmail")]
    public async Task UpdateAsync_ValidatesOutsideTheUi(string name, string? email, string? phone, string? address, string field)
    {
        var (id, actor) = await SeedAsync();
        await using var database = fixture.CreateUnresolvedContext();
        var result = await Writer(database, actor).UpdateAsync(id, new(name, email, phone, address, Guid.Empty));
        Assert.Equal(AssociationWriteOutcome.InvalidInput, result.Outcome);
        Assert.Equal(field, result.InvalidField);
        Assert.False(await database.PlatformAuditLogs.AnyAsync(x => x.AssociationId == id));
    }

    private async Task<(int Id, string Actor)> SeedAsync()
    {
        await using var database = fixture.CreateUnresolvedContext();
        var actor = Guid.NewGuid().ToString();
        database.Users.Add(new ApplicationUser { Id = actor, FirstName = "Test", LastName = "Operator" });
        var association = new Association { Name = "Original", Slug = "test-" + Guid.NewGuid().ToString("N") };
        database.Associations.Add(association);
        await database.SaveChangesAsync();
        return (association.Id, actor);
    }

    private static PlatformAssociationWriter Writer(ApplicationDbContext database, string actor) =>
        new(database, new Access(actor), TimeProvider.System, NullLogger<PlatformAssociationWriter>.Instance);

    private sealed class Access(string actor) : IPlatformAssociationWriteAccess
    {
        public Task<string> GetOperatorIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(actor);
    }

    private sealed class UpdateCommandCapture : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Updates { get; } = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE [Associations]", StringComparison.Ordinal))
                Updates.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrived;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
    }
}
