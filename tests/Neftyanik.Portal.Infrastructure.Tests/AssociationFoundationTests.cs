using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class AssociationFoundationTests : IClassFixture<AssociationDatabaseFixture>
{
    private readonly AssociationDatabaseFixture _database;

    public AssociationFoundationTests(AssociationDatabaseFixture database) => _database = database;

    [Fact]
    public async Task SaveChangesAsync_AssignsNeftyanikToEveryBusinessEntityAndPreservesRelationships()
    {
        await using var context = _database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), FirstName = "Test", LastName = "User" };
        context.Users.Add(user);
        var graph = CreateBusinessGraph(0, user.Id);
        context.AddRange(graph);
        await context.SaveChangesAsync();
        var associationId = await context.Associations.Where(x => x.Slug == "neftyanik").Select(x => x.Id).SingleAsync();
        Assert.Equal(22, graph.Length);
        Assert.All(graph, entity => Assert.Equal(associationId, entity.AssociationId));

        var plot = graph.OfType<Plot>().Single();
        plot.MemberElectricityMeter = graph.OfType<MemberElectricityMeter>().Single();
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var stored = await context.Plots.AsNoTracking().Include(x => x.MemberElectricityMeter).SingleAsync(x => x.Id == plot.Id);
        Assert.Equal(associationId, stored.MemberElectricityMeter!.AssociationId);
    }

    [Fact]
    public async Task SaveChangesAsync_ResolvesBySlugRatherThanSeedId()
    {
        await using var context = _database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var seeded = await context.Associations.SingleAsync(x => x.Slug == "neftyanik");
        seeded.Slug = "previous-slug";
        await context.SaveChangesAsync();
        var replacement = new Association { Name = "Нефтяник", Slug = "neftyanik" };
        context.Associations.Add(replacement);
        await context.SaveChangesAsync();
        var plot = new Plot { Number = "lookup-by-slug" };
        context.Plots.Add(plot);
        await context.SaveChangesAsync();
        Assert.NotEqual(seeded.Id, replacement.Id);
        Assert.Equal(replacement.Id, plot.AssociationId);
    }

    [Fact]
    public async Task SaveChangesAsync_PreservesExplicitIdAndNewAssociationNavigation()
    {
        await using var context = _database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var other = new Association { Name = "Other", Slug = "other" };
        var plot = new Plot { Number = "explicit-navigation", Association = other };
        context.Plots.Add(plot);
        await context.SaveChangesAsync();
        var second = new Plot { Number = "explicit-id", AssociationId = other.Id };
        context.Plots.Add(second);
        await context.SaveChangesAsync();
        Assert.Equal(other.Id, plot.AssociationId);
        Assert.Equal(other.Id, second.AssociationId);
        Assert.NotEqual(await context.Associations.Where(x => x.Slug == "neftyanik").Select(x => x.Id).SingleAsync(), other.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChangesAsync_RejectsImplicitOwnershipWhenNeftyanikIsMissingOrInactive(bool inactive)
    {
        await using var context = _database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var initial = await context.Associations.SingleAsync(x => x.Slug == "neftyanik");
        if (inactive)
        {
            initial.IsActive = false;
        }
        else
        {
            initial.Slug = "renamed";
        }
        await context.SaveChangesAsync();
        context.Plots.Add(new Plot { Number = "must-not-save" });
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("existing active 'neftyanik'", exception.Message);
        Assert.True(context.ChangeTracker.AutoDetectChangesEnabled);
        Assert.False(await context.Plots.AsNoTracking().AnyAsync(x => x.Number == "must-not-save"));
        Assert.Equal(1, await context.Associations.CountAsync());
    }

    [Theory]
    [InlineData("plot")]
    [InlineData("setting")]
    [InlineData("charge-code")]
    [InlineData("default-charge")]
    [InlineData("membership-year")]
    [InlineData("member-tariff")]
    [InlineData("association-tariff")]
    [InlineData("association-reading")]
    [InlineData("association-initial-reading")]
    public async Task BusinessIdentifier_IsUniqueWithinAssociationButAllowedAcrossAssociations(string kind)
    {
        await using var context = _database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var initialId = await context.Associations.Where(x => x.Slug == "neftyanik").Select(x => x.Id).SingleAsync();
        var other = new Association { Name = "Other", Slug = "other" };
        context.Associations.Add(other);
        await context.SaveChangesAsync();
        context.Add(CreateIdentifier(kind, initialId, 1));
        context.Add(CreateIdentifier(kind, other.Id, 2));
        await context.SaveChangesAsync();
        context.Add(CreateIdentifier(kind, initialId, 3));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains(((SqlException)exception.InnerException!).Number, new[] { 2601, 2627 });
    }

    [Fact]
    public async Task AllCompositeTenantForeignKeys_RejectCrossAssociationConnectionsInSqlServer()
    {
        await using var context = _database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var initialId = await context.Associations.Where(x => x.Slug == "neftyanik").Select(x => x.Id).SingleAsync();
        var other = new Association { Name = "Other", Slug = "other" };
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), FirstName = "Test", LastName = "User" };
        context.AddRange(other, user);
        await context.SaveChangesAsync();
        var first = CreateBusinessGraph(initialId, user.Id);
        var second = CreateBusinessGraph(other.Id, user.Id);
        context.AddRange(first);
        context.AddRange(second);
        await context.SaveChangesAsync();
        var tested = 0;
        foreach (var dependent in first)
        {
            var entityType = context.Model.FindEntityType(dependent.GetType())!;
            foreach (var foreignKey in entityType.GetForeignKeys().Where(x => typeof(IAssociationOwned).IsAssignableFrom(x.PrincipalEntityType.ClrType)))
            {
                Assert.Equal(new[] { "AssociationId", "Id" }, foreignKey.PrincipalKey.Properties.Select(x => x.Name));
                Assert.Equal("AssociationId", foreignKey.Properties[0].Name);
                Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
                var principal = second.Single(x => x.GetType() == foreignKey.PrincipalEntityType.ClrType);
                var principalId = principal.GetType().GetProperty("Id")!.GetValue(principal);
                var dependentId = dependent.GetType().GetProperty("Id")!.GetValue(dependent);
                var sql = $"UPDATE [{entityType.GetTableName()}] SET [{foreignKey.Properties[1].GetColumnName()}] = @principalId WHERE [Id] = @dependentId";
                var exception = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(sql,
                    new SqlParameter("@principalId", principalId), new SqlParameter("@dependentId", dependentId)));
                Assert.Equal(547, exception.Number);
                Assert.Contains(foreignKey.GetConstraintName()!, exception.Message);
                tested++;
            }
        }
        Assert.Equal(18, tested);
    }

    [Fact]
    public async Task Model_HasOwnershipForAllBusinessTypesAndNoFiltersOrDuplicateIndexes()
    {
        await using var context = _database.CreateContext();
        var entities = context.Model.GetEntityTypes().ToArray();
        var owned = entities.Where(x => typeof(IAssociationOwned).IsAssignableFrom(x.ClrType)).ToArray();
        Assert.Equal(22, owned.Length);
        foreach (var entity in entities)
        {
            Assert.Null(entity.GetQueryFilter());
            var indexes = entity.GetIndexes().Select(x => string.Join(",", x.Properties.Select(p => p.Name))).ToArray();
            Assert.Equal(indexes.Length, indexes.Distinct().Count());
        }
        foreach (var entity in owned)
        {
            Assert.False(entity.FindProperty("AssociationId")!.IsNullable);
            var associationKey = Assert.Single(entity.GetForeignKeys().Where(x => x.PrincipalEntityType.ClrType == typeof(Association)));
            Assert.Equal(DeleteBehavior.Restrict, associationKey.DeleteBehavior);
            Assert.All(entity.GetIndexes().Where(x => x.IsUnique), index => Assert.Equal("AssociationId", index.Properties[0].Name));
        }
        Assert.Null(context.Model.FindEntityType(typeof(ApplicationUser))!.FindProperty("AssociationId"));
        Assert.Null(context.Model.FindEntityType(typeof(UserLoginHistory))!.FindProperty("AssociationId"));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Uppercase")]
    [InlineData("has space")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("two--hyphens")]
    [InlineData("нефтяник")]
    public async Task SaveChangesAsync_RejectsInvalidAssociationSlug(string slug)
    {
        await using var context = _database.CreateContext();
        context.Associations.Add(new Association { Name = "Invalid", Slug = slug });
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    private static IAssociationOwned CreateIdentifier(string kind, int associationId, int sequence)
    {
        IAssociationOwned entity = kind switch
        {
            "plot" => new Plot { Number = "same" },
            "setting" => new SystemSetting { Key = "same", Value = "value" },
            "charge-code" => new ChargeType { Name = "Same", Code = "SAME" },
            "default-charge" => new ChargeType { Name = "Same", IsDefault = true },
            "membership-year" => new MembershipFeeRate { Year = 2040, AmountPerPlot = 500m },
            "member-tariff" => new MemberElectricityTariff { EffectiveFrom = new DateOnly(2040, 1, 1), Rate = 5m },
            "association-tariff" => new AssociationElectricityTariff { EffectiveFrom = new DateOnly(2040, 1, 1), DayRate = 5m, NightRate = 2.5m },
            "association-reading" => new AssociationElectricityReading { ReadingDate = new DateOnly(2040, 1, 1) },
            "association-initial-reading" => new AssociationElectricityReading { ReadingDate = new DateOnly(2040, 1, sequence), IsInitialReading = true },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        entity.AssociationId = associationId;
        return entity;
    }

    internal static IAssociationOwned[] CreateBusinessGraph(int associationId, string userId)
    {
        var member = new Member { FullName = "Member", ApplicationUserId = userId };
        var plot = new Plot { Number = "123" };
        var type = new ChargeType { Name = "Electricity", Code = "TEST" };
        var category = new ExpenseCategory { Name = "Custom expense" };
        var associationReading = new AssociationElectricityReading { ReadingDate = new DateOnly(2040, 1, 1), IsInitialReading = true };
        var meter = new MemberElectricityMeter { Member = member, BillingPlot = plot, MeterNumber = "meter" };
        var charge = new Charge { Plot = plot, ChargeType = type, Amount = 100m, ChargeDate = new DateOnly(2040, 1, 1) };
        var payment = new Payment { Member = member, Plot = plot, Amount = 50m, PaymentDate = new DateOnly(2040, 1, 2) };
        IAssociationOwned[] graph =
        [
            member, plot, type, category, associationReading,
            new AssociationElectricityTariff { EffectiveFrom = new DateOnly(2040, 1, 1), DayRate = 5m, NightRate = 2.5m },
            new MemberElectricityTariff { EffectiveFrom = new DateOnly(2040, 1, 1), Rate = 5m },
            new MembershipFeeRate { Year = 2040, AmountPerPlot = 500m },
            new NewsArticle { Title = "News", Content = "Content", CreatedByUserId = userId },
            new AssociationDocument { Title = "Document", FilePath = "test.pdf", OriginalFileName = "test.pdf", UploadedByUserId = userId },
            new SystemSetting { Key = "test", Value = "preserve" },
            new AuditLog { Action = "Test", EntityType = "Plot", EntityId = "legacy", UserId = userId },
            new FinancialAuditLog { Action = "Test", EntityType = "Payment", EntityId = "legacy", Description = "preserve" },
            new PlotOwnershipHistory { Plot = plot, OwnerId = userId, ValidFrom = DateTimeOffset.UtcNow },
            new PlotOwnership { Plot = plot, Member = member },
            meter, charge,
            new MemberElectricityReading { MemberElectricityMeter = meter, Charge = charge, ReadingDate = new DateOnly(2040, 1, 1), CurrentReading = 100m },
            payment,
            new PaymentAllocation { Payment = payment, Charge = charge, Amount = 50m },
            new PaymentNotification { Member = member, Payment = payment, Amount = 50m },
            new Expense { ExpenseCategory = category, AssociationElectricityReading = associationReading, Amount = 25m, ExpenseDate = new DateOnly(2040, 1, 1), Description = "preserve", CreatedByUserId = userId }
        ];
        foreach (var entity in graph)
        {
            entity.AssociationId = associationId;
        }
        return graph;
    }
}

public sealed class AssociationDatabaseFixture : IAsyncLifetime
{
    private readonly DbContextOptions<ApplicationDbContext> _options = CreateOptions();

    internal static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($"Server=(localdb)\\mssqllocaldb;Database=NeftyanikAssociationTests_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

    public ApplicationDbContext CreateContext() => new(_options);

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
