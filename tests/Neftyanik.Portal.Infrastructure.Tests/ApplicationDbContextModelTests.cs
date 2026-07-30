using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class ApplicationDbContextModelTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=NeftyanikPortalModelTests;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public void Plot_HasUniqueIndex_OnNumber()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(Plot));

        var index = entityType!.GetIndexes().Single(x => x.Properties.Select(p => p.Name).SequenceEqual([nameof(Plot.Number)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void MemberElectricityReading_HasUniqueCompositeIndex_OnMeterIdAndReadingDate()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(MemberElectricityReading));

        var index = entityType!.GetIndexes().Single(x => x.Properties.Select(p => p.Name).SequenceEqual([nameof(MemberElectricityReading.MemberElectricityMeterId), nameof(MemberElectricityReading.ReadingDate)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Plot_HasOptionalMemberElectricityMeterRelationship()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(Plot))!;
        var foreignKey = entityType.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(MemberElectricityMeter));
        var property = entityType.FindProperty(nameof(Plot.MemberElectricityMeterId));

        Assert.NotNull(property);
        Assert.True(property!.IsNullable);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void MembershipFeeRate_HasUniqueIndex_OnYear()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(MembershipFeeRate));

        var index = entityType!.GetIndexes().Single(x => x.Properties.Select(p => p.Name).SequenceEqual([nameof(MembershipFeeRate.Year)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void SystemSetting_HasUniqueIndex_OnKey()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(SystemSetting));

        var index = entityType!.GetIndexes().Single(x => x.Properties.Select(p => p.Name).SequenceEqual([nameof(SystemSetting.Key)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void LegacyElectricityTables_AreNotIncludedInModel()
    {
        using var context = CreateContext();

        var tableNames = context.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => tableName is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("ElectricityMeters", tableNames);
        Assert.DoesNotContain("MeterPlots", tableNames);
        Assert.DoesNotContain("MeterReadings", tableNames);
        Assert.DoesNotContain("ElectricityReadings", tableNames);
        Assert.DoesNotContain("ElectricityTariffs", tableNames);
    }

    [Fact]
    public void Charge_Amount_HasMoneyPrecision()
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(Charge))!.FindProperty(nameof(Charge.Amount));

        Assert.Equal(18, property!.GetPrecision());
        Assert.Equal(2, property.GetScale());
    }

    [Fact]
    public void MemberElectricityReading_Values_HaveExpectedPrecision()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(MemberElectricityReading))!;

        AssertPropertyPrecision(entityType, nameof(MemberElectricityReading.CurrentReading), 18, 3);
        AssertPropertyPrecision(entityType, nameof(MemberElectricityReading.AppliedMemberRate), 18, 4);
        AssertPropertyPrecision(entityType, nameof(MemberElectricityReading.Amount), 18, 2);
    }

    [Fact]
    public void PaymentAllocation_Relationships_UseRestrictiveDeleteBehavior()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(PaymentAllocation))!;

        var paymentForeignKey = entityType.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(Payment));
        var chargeForeignKey = entityType.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(Charge));

        Assert.Equal(DeleteBehavior.Restrict, paymentForeignKey.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, chargeForeignKey.DeleteBehavior);
    }

    [Fact]
    public void KeyFinancialRelationships_UseRestrictiveDeleteBehavior()
    {
        using var context = CreateContext();

        AssertDeleteBehavior<MemberElectricityReading, MemberElectricityMeter>(context, DeleteBehavior.Restrict);
        AssertDeleteBehavior<Expense, ExpenseCategory>(context, DeleteBehavior.Restrict);
        AssertDeleteBehavior<Charge, Plot>(context, DeleteBehavior.Restrict);
        AssertDeleteBehavior<Charge, ApplicationUser>(context, DeleteBehavior.Restrict, nameof(Charge.CreatedByUserId));
        AssertDeleteBehavior<Payment, ApplicationUser>(context, DeleteBehavior.Restrict, nameof(Payment.CreatedByUserId));
    }

    private static void AssertPropertyPrecision(IEntityType entityType, string propertyName, int precision, int scale)
    {
        var property = entityType.FindProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(precision, property!.GetPrecision());
        Assert.Equal(scale, property.GetScale());
    }

    private static void AssertDeleteBehavior<TDependent, TPrincipal>(ApplicationDbContext context, DeleteBehavior expectedDeleteBehavior, string? foreignKeyPropertyName = null)
    {
        var entityType = context.Model.FindEntityType(typeof(TDependent))!;
        var foreignKey = entityType.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(TPrincipal)
            && (foreignKeyPropertyName == null || x.Properties.Any(p => p.Name == foreignKeyPropertyName)));

        Assert.Equal(expectedDeleteBehavior, foreignKey.DeleteBehavior);
    }
}
