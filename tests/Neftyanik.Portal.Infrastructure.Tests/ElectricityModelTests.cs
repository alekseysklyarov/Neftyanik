using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using System.Linq;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class ElectricityModelTests
{
    [Fact]
    public void ElectricityReading_HasUniqueCompositeIndex_OnPlotIdAndReadingDate()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(ElectricityReading));

        var index = entityType!.GetIndexes().Single(x => x.Properties.Select(p => p.Name).SequenceEqual([nameof(ElectricityReading.PlotId), nameof(ElectricityReading.ReadingDate)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void ElectricityTariff_HasUniqueIndex_OnEffectiveFrom()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(ElectricityTariff));

        var index = entityType!.GetIndexes().Single(x => x.Properties.Select(p => p.Name).SequenceEqual([nameof(ElectricityTariff.EffectiveFrom)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void ElectricityReading_Properties_HaveExpectedPrecision()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(ElectricityReading))!;

        AssertPropertyPrecision(entityType, nameof(ElectricityReading.CurrentDayReading), 18, 3);
        AssertPropertyPrecision(entityType, nameof(ElectricityReading.CurrentNightReading), 18, 3);
        AssertPropertyPrecision(entityType, nameof(ElectricityReading.DayConsumption), 18, 3);
        AssertPropertyPrecision(entityType, nameof(ElectricityReading.NightConsumption), 18, 3);
        AssertPropertyPrecision(entityType, nameof(ElectricityReading.DayRate), 18, 4);
        AssertPropertyPrecision(entityType, nameof(ElectricityReading.NightRate), 18, 4);
        AssertPropertyPrecision(entityType, nameof(ElectricityReading.DayAmount), 18, 2);
        AssertPropertyPrecision(entityType, nameof(ElectricityReading.NightAmount), 18, 2);
        AssertPropertyPrecision(entityType, nameof(ElectricityReading.TotalAmount), 18, 2);
    }

    [Fact]
    public void ElectricityTariff_Rates_HaveExpectedPrecision()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(ElectricityTariff))!;

        AssertPropertyPrecision(entityType, nameof(ElectricityTariff.DayRate), 18, 4);
        AssertPropertyPrecision(entityType, nameof(ElectricityTariff.NightRate), 18, 4);
    }

    [Fact]
    public void ElectricityRelationships_UseRestrictiveDeleteBehavior()
    {
        using var context = CreateContext();

        AssertDeleteBehavior<ElectricityReading, Plot>(context, DeleteBehavior.Restrict);
        AssertDeleteBehavior<ElectricityReading, Charge>(context, DeleteBehavior.Restrict);
        AssertDeleteBehavior<ElectricityReading, ApplicationUser>(context, DeleteBehavior.Restrict, nameof(ElectricityReading.CreatedByUserId));
        AssertDeleteBehavior<ElectricityTariff, ApplicationUser>(context, DeleteBehavior.Restrict, nameof(ElectricityTariff.CreatedByUserId));
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
