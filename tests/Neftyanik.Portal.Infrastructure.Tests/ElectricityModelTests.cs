using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using System.Linq;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class ElectricityModelTests
{
    [Fact]
    public void AssociationElectricityReading_HasUniqueIndex_OnReadingDate()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(AssociationElectricityReading));

        var index = entityType!.GetIndexes().Single(x => x.Properties.Select(p => p.Name).SequenceEqual([nameof(AssociationElectricityReading.ReadingDate)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void AssociationElectricityTariff_HasUniqueIndex_OnEffectiveFrom()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(AssociationElectricityTariff));

        var index = entityType!.GetIndexes().Single(x => x.Properties.Select(p => p.Name).SequenceEqual([nameof(AssociationElectricityTariff.EffectiveFrom)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void AssociationElectricityReading_Properties_HaveExpectedPrecision()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(AssociationElectricityReading))!;

        AssertPropertyPrecision(entityType, nameof(AssociationElectricityReading.CurrentDayReading), 18, 3);
        AssertPropertyPrecision(entityType, nameof(AssociationElectricityReading.CurrentNightReading), 18, 3);
        AssertPropertyPrecision(entityType, nameof(AssociationElectricityReading.DayConsumption), 18, 3);
        AssertPropertyPrecision(entityType, nameof(AssociationElectricityReading.NightConsumption), 18, 3);
        AssertPropertyPrecision(entityType, nameof(AssociationElectricityReading.TotalConsumption), 18, 3);
        AssertPropertyPrecision(entityType, nameof(AssociationElectricityReading.AppliedSupplierDayRate), 18, 4);
        AssertPropertyPrecision(entityType, nameof(AssociationElectricityReading.AppliedSupplierNightRate), 18, 4);
        AssertPropertyPrecision(entityType, nameof(AssociationElectricityReading.DayAmount), 18, 2);
        AssertPropertyPrecision(entityType, nameof(AssociationElectricityReading.NightAmount), 18, 2);
        AssertPropertyPrecision(entityType, nameof(AssociationElectricityReading.TotalSupplierAmount), 18, 2);
    }

    [Fact]
    public void CurrentElectricityTariffs_HaveExpectedPrecision()
    {
        using var context = CreateContext();
        var associationTariffType = context.Model.FindEntityType(typeof(AssociationElectricityTariff))!;
        var memberTariffType = context.Model.FindEntityType(typeof(MemberElectricityTariff))!;

        AssertPropertyPrecision(associationTariffType, nameof(AssociationElectricityTariff.DayRate), 18, 4);
        AssertPropertyPrecision(associationTariffType, nameof(AssociationElectricityTariff.NightRate), 18, 4);
        AssertPropertyPrecision(memberTariffType, nameof(MemberElectricityTariff.Rate), 18, 4);
        AssertPropertyPrecision(memberTariffType, nameof(MemberElectricityTariff.NightRate), 18, 4);
    }

    [Fact]
    public void MemberElectricityReading_Properties_HaveExpectedPrecision()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(MemberElectricityReading))!;

        AssertPropertyPrecision(entityType, nameof(MemberElectricityReading.CurrentReading), 18, 3);
        AssertPropertyPrecision(entityType, nameof(MemberElectricityReading.CurrentNightReading), 18, 3);
        AssertPropertyPrecision(entityType, nameof(MemberElectricityReading.AppliedMemberRate), 18, 4);
        AssertPropertyPrecision(entityType, nameof(MemberElectricityReading.AppliedMemberNightRate), 18, 4);
        AssertPropertyPrecision(entityType, nameof(MemberElectricityReading.Amount), 18, 2);
    }

    [Fact]
    public void Member_ElectricityMeterType_HasSingleRateDefault()
    {
        using var context = CreateContext();
        var memberType = context.Model.FindEntityType(typeof(Member))!;
        var property = memberType.FindProperty(nameof(Member.ElectricityMeterType));

        Assert.NotNull(property);
        Assert.Equal(MemberElectricityMeterType.SingleRate, property!.GetDefaultValue());
    }

    [Fact]
    public void CurrentElectricityRelationships_UseRestrictiveDeleteBehavior()
    {
        using var context = CreateContext();

        AssertDeleteBehavior<AssociationElectricityReading, ApplicationUser>(context, DeleteBehavior.Restrict, nameof(AssociationElectricityReading.CreatedByUserId));
        AssertDeleteBehavior<AssociationElectricityTariff, ApplicationUser>(context, DeleteBehavior.Restrict, nameof(AssociationElectricityTariff.CreatedByUserId));
        AssertDeleteBehavior<MemberElectricityMeter, Member>(context, DeleteBehavior.Restrict);
        AssertDeleteBehavior<MemberElectricityMeter, Plot>(context, DeleteBehavior.Restrict, nameof(MemberElectricityMeter.BillingPlotId));
        AssertDeleteBehavior<MemberElectricityReading, MemberElectricityMeter>(context, DeleteBehavior.Restrict);
        AssertDeleteBehavior<MemberElectricityReading, Charge>(context, DeleteBehavior.Restrict);
        AssertDeleteBehavior<MemberElectricityReading, ApplicationUser>(context, DeleteBehavior.Restrict, nameof(MemberElectricityReading.CreatedByUserId));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=NeftyanikPortalElectricityModelTests;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new ApplicationDbContext(options);
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
