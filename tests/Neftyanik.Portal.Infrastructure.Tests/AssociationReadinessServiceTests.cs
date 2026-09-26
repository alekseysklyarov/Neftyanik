using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Infrastructure.Services;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public sealed class AssociationReadinessServiceTests(AssociationDatabaseFixture fixture) : IClassFixture<AssociationDatabaseFixture>
{
    private static readonly DateOnly Today = new(2026, 9, 25);

    [Fact]
    public async Task GetAsync_EmptyTenant_ReturnsIndependentChecksWithoutWritingOrTracking()
    {
        await using var db = await CreateTenantAsync();
        var result = await ReadAsync(db);
        Assert.Equal(Today, result.AsOfDate);
        Assert.Equal(9, result.Checks.Count);
        Assert.Equal(new AssociationReadinessFacts(), result.Facts);
        AssertCheck(result, ReadinessArea.IndividualMeters, ReadinessStatus.NotUsed, ReadinessReason.NoMeters);
        AssertCheck(result, ReadinessArea.Cash, ReadinessStatus.Warning, ReadinessReason.CashStartsAtZero);
        AssertCheck(result, ReadinessArea.ChargeTypes, ReadinessStatus.RequiredForOperation, ReadinessReason.NoActiveChargeTypes);
        AssertCheck(result, ReadinessArea.MembershipFeeRates, ReadinessStatus.NotImplemented, ReadinessReason.MembershipRatesNotIntegrated);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.False(await db.SystemSettings.AnyAsync());
        Assert.False(await db.ChargeTypes.AnyAsync());
        Assert.False(await db.FinancialAuditLogs.AnyAsync());
    }

    [Fact]
    public async Task GetAsync_FutureTariffsOnly_DoesNotTreatThemAsEffectiveToday()
    {
        await using var db = await CreateTenantAsync();
        db.MemberElectricityMeters.Add(Meter(initial: true));
        db.MemberElectricityTariffs.Add(new() { EffectiveFrom = Today.AddDays(1), Rate = 5, NightRate = 2 });
        db.AssociationElectricityTariffs.Add(new() { EffectiveFrom = Today.AddDays(1), DayRate = 5, NightRate = 2 });
        db.AssociationElectricityReadings.Add(new() { ReadingDate = Today.AddDays(-1), IsInitialReading = true });
        await db.SaveChangesAsync();
        var result = await ReadAsync(db);
        AssertCheck(result, ReadinessArea.MemberElectricity, ReadinessStatus.RequiredForOperation, ReadinessReason.NoApplicableTariff);
        AssertCheck(result, ReadinessArea.SupplierElectricity, ReadinessStatus.RequiredForOperation, ReadinessReason.NoApplicableTariff);
        Assert.Null(result.Facts.MemberTariffEffectiveFrom);
        Assert.Null(result.Facts.SupplierTariffEffectiveFrom);
        Assert.Equal(1, result.Facts.MetersWithoutApplicableTariff);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task GetAsync_CurrentTariff_IncludingZeroRate_IsReadyWithoutNightRateForSingleRateMeter(int rate)
    {
        await using var db = await CreateTenantAsync();
        db.MemberElectricityMeters.Add(Meter(initial: true));
        db.MemberElectricityTariffs.Add(new() { EffectiveFrom = Today, Rate = rate });
        db.MemberElectricityTariffs.Add(new() { EffectiveFrom = Today.AddDays(1), Rate = 9 });
        db.AssociationElectricityTariffs.Add(new() { EffectiveFrom = Today, DayRate = rate, NightRate = 0 });
        db.AssociationElectricityReadings.Add(new() { ReadingDate = Today, IsInitialReading = true });
        await db.SaveChangesAsync();
        var result = await ReadAsync(db);
        AssertCheck(result, ReadinessArea.MemberElectricity, ReadinessStatus.Ready, ReadinessReason.Configured);
        AssertCheck(result, ReadinessArea.SupplierElectricity, ReadinessStatus.Ready, ReadinessReason.Configured);
        AssertCheck(result, ReadinessArea.IndividualMeters, ReadinessStatus.Ready, ReadinessReason.Configured);
        Assert.Equal(Today, result.Facts.MemberTariffEffectiveFrom);
        Assert.False(result.Facts.NeedsNightTariff);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, false)]
    [InlineData(2, false)]
    public async Task GetAsync_DayNightMeter_RequiresNightRateOnlyWhenMissing(int? nightRate, bool missing)
    {
        await using var db = await CreateTenantAsync();
        db.MemberElectricityMeters.Add(Meter(MemberElectricityMeterType.DayNight, initial: true));
        db.MemberElectricityTariffs.Add(new() { EffectiveFrom = Today.AddDays(-1), Rate = 5, NightRate = 2 });
        db.MemberElectricityTariffs.Add(new() { EffectiveFrom = Today, Rate = 5, NightRate = nightRate });
        await db.SaveChangesAsync();
        var result = await ReadAsync(db);
        Assert.True(result.Facts.NeedsNightTariff);
        Assert.Equal(missing ? 1 : 0, result.Facts.MetersWithoutApplicableTariff);
        AssertCheck(result, ReadinessArea.MemberElectricity,
            missing ? ReadinessStatus.RequiredForOperation : ReadinessStatus.Ready,
            missing ? ReadinessReason.MissingNightTariff : ReadinessReason.Configured);
    }

    [Fact]
    public async Task GetAsync_NoActiveMeters_DoesNotRequireNightTariff()
    {
        await using var db = await CreateTenantAsync();
        var meter = Meter(MemberElectricityMeterType.DayNight);
        meter.IsActive = false;
        db.Add(meter);
        db.MemberElectricityTariffs.Add(new() { EffectiveFrom = Today, Rate = 5 });
        await db.SaveChangesAsync();
        var result = await ReadAsync(db);
        AssertCheck(result, ReadinessArea.IndividualMeters, ReadinessStatus.NotUsed, ReadinessReason.NoMeters);
        AssertCheck(result, ReadinessArea.MemberElectricity, ReadinessStatus.Ready, ReadinessReason.Configured);
        Assert.False(result.Facts.NeedsNightTariff);
        Assert.Equal(0, result.Facts.ActiveMeters);
    }

    [Fact]
    public async Task GetAsync_MeterWithoutInitialReading_ReportsCountEvenWithOrdinaryReading()
    {
        await using var db = await CreateTenantAsync();
        var meter = Meter();
        meter.Readings.Add(new() { ReadingDate = Today, CurrentReading = 100 });
        db.Add(meter);
        db.MemberElectricityMeters.Add(Meter(initial: true));
        db.MemberElectricityTariffs.Add(new() { EffectiveFrom = Today, Rate = 5 });
        await db.SaveChangesAsync();
        var result = await ReadAsync(db);
        Assert.Equal(2, result.Facts.ActiveMeters);
        Assert.Equal(1, result.Facts.MetersWithoutInitialReading);
        Assert.Equal(0, result.Facts.MetersWithoutApplicableTariff);
        AssertCheck(result, ReadinessArea.IndividualMeters, ReadinessStatus.RequiredForOperation, ReadinessReason.IncompleteMeters);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsync_SupplierHistoryWithoutInitialReading_ExplainsMissingPrerequisites(bool tariff)
    {
        await using var db = await CreateTenantAsync();
        db.AssociationElectricityReadings.Add(new() { ReadingDate = Today });
        if (tariff) db.AssociationElectricityTariffs.Add(new() { EffectiveFrom = Today, DayRate = 5, NightRate = 2 });
        await db.SaveChangesAsync();
        var result = await ReadAsync(db);
        Assert.True(result.Facts.HasSupplierHistory);
        Assert.False(result.Facts.HasSupplierInitialReading);
        AssertCheck(result, ReadinessArea.SupplierElectricity, ReadinessStatus.RequiredForOperation,
            tariff ? ReadinessReason.MissingInitialReading : ReadinessReason.MissingTariffAndInitialReading);
    }

    [Fact]
    public async Task GetAsync_SystemCategory_IsNotAManualCategory()
    {
        await using var db = await CreateTenantAsync();
        var category = new ExpenseCategory { Name = "Electricity", IsActive = true };
        db.Add(category);
        await db.SaveChangesAsync();
        db.SystemSettings.Add(new() { Key = ElectricityExpenseCategoryQueries.SettingKey, Value = category.Id.ToString() });
        await db.SaveChangesAsync();
        var result = await ReadAsync(db);
        AssertCheck(result, ReadinessArea.ElectricityExpenseCategory, ReadinessStatus.Ready, ReadinessReason.Configured);
        AssertCheck(result, ReadinessArea.ManualExpenseCategories, ReadinessStatus.RequiredForOperation, ReadinessReason.NoManualCategories);
        db.ExpenseCategories.Add(new() { Name = "Repairs", IsActive = true });
        db.ExpenseCategories.Add(new() { Name = "Archived", IsActive = false });
        await db.SaveChangesAsync();
        result = await ReadAsync(db);
        Assert.Equal(1, result.Facts.ActiveManualExpenseCategories);
        AssertCheck(result, ReadinessArea.ManualExpenseCategories, ReadinessStatus.Ready, ReadinessReason.Configured);
    }

    [Theory]
    [InlineData(null, ReadinessReason.MissingCategorySetting)]
    [InlineData("", ReadinessReason.InvalidCategorySetting)]
    [InlineData("not-an-id", ReadinessReason.InvalidCategorySetting)]
    [InlineData("0", ReadinessReason.InvalidCategorySetting)]
    [InlineData("-1", ReadinessReason.InvalidCategorySetting)]
    [InlineData("2147483647", ReadinessReason.CategoryUnavailable)]
    public async Task GetAsync_MissingOrDamagedCategorySetting_ReportsSpecificReason(string? value, ReadinessReason reason)
    {
        await using var db = await CreateTenantAsync();
        if (value is not null)
        {
            db.SystemSettings.Add(new() { Key = ElectricityExpenseCategoryQueries.SettingKey, Value = value });
            await db.SaveChangesAsync();
        }
        var result = await ReadAsync(db);
        AssertCheck(result, ReadinessArea.ElectricityExpenseCategory, ReadinessStatus.RequiredForOperation, reason);
    }

    [Fact]
    public async Task GetAsync_ArchivedSystemCategory_IsUnavailable()
    {
        await using var db = await CreateTenantAsync();
        var category = new ExpenseCategory { Name = "Archived electricity", IsActive = false };
        db.Add(category);
        await db.SaveChangesAsync();
        db.SystemSettings.Add(new() { Key = ElectricityExpenseCategoryQueries.SettingKey, Value = category.Id.ToString() });
        await db.SaveChangesAsync();
        AssertCheck(await ReadAsync(db), ReadinessArea.ElectricityExpenseCategory, ReadinessStatus.RequiredForOperation, ReadinessReason.CategoryUnavailable);
    }

    [Fact]
    public async Task GetAsync_CashInitialization_ReturnsDateWithoutFinancialDetails()
    {
        await using var db = await CreateTenantAsync();
        db.SystemSettings.Add(new() { Key = "Finance.CashInitialization", Value = "{\"Amount\":0,\"AcceptedAt\":\"2026-09-25\",\"AcceptedFrom\":\"Private name\"}" });
        await db.SaveChangesAsync();
        var result = await ReadAsync(db);
        Assert.Equal(Today, result.Facts.CashInitializedOn);
        AssertCheck(result, ReadinessArea.Cash, ReadinessStatus.Ready, ReadinessReason.Configured);
        Assert.DoesNotContain("Private name", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task GetAsync_YearlyTypesAndMembershipRates_DoesNotConnectRatesToBilling()
    {
        await using var db = await CreateTenantAsync();
        db.ChargeTypes.Add(new() { Code = "ROAD", Name = "Yearly road repair", IsActive = true, IsYearly = true });
        db.ChargeTypes.Add(new() { Code = "OLD", Name = "Archived", IsActive = false, IsYearly = true });
        db.MembershipFeeRates.Add(new() { Year = 2026, AmountPerPlot = 500 });
        await db.SaveChangesAsync();
        var result = await ReadAsync(db);
        Assert.Equal(1, result.Facts.ActiveChargeTypes);
        Assert.Equal(1, result.Facts.YearlyChargeTypes);
        AssertCheck(result, ReadinessArea.MembershipFeeRates, ReadinessStatus.NotImplemented, ReadinessReason.MembershipRatesNotIntegrated);
        Assert.False(await db.Charges.AnyAsync());
    }

    [Fact]
    public async Task GetAsync_MultipleTenants_IsolatesEveryReadinessAreaAndRejectsForeignCategoryMapping()
    {
        await using var populated = await CreateTenantAsync();
        var meter = Meter(MemberElectricityMeterType.DayNight, initial: true);
        populated.Add(meter);
        populated.MemberElectricityTariffs.Add(new() { EffectiveFrom = Today, Rate = 5, NightRate = 2 });
        populated.AssociationElectricityTariffs.Add(new() { EffectiveFrom = Today, DayRate = 5, NightRate = 2 });
        populated.AssociationElectricityReadings.Add(new() { ReadingDate = Today, IsInitialReading = true });
        populated.ChargeTypes.Add(new() { Code = "ROAD", Name = "Yearly road", IsActive = true, IsYearly = true });
        var category = new ExpenseCategory { Name = "Electricity", IsActive = true };
        populated.Add(category);
        populated.ExpenseCategories.Add(new() { Name = "Manual", IsActive = true });
        populated.SystemSettings.Add(new() { Key = "Finance.CashInitialization", Value = "{\"Amount\":0,\"AcceptedAt\":\"2026-09-25\",\"AcceptedFrom\":\"Private\"}" });
        await populated.SaveChangesAsync();
        populated.PlotOwnerships.Add(new() { MemberId = meter.MemberId, PlotId = meter.BillingPlotId, ValidFrom = Today });
        populated.SystemSettings.Add(new() { Key = ElectricityExpenseCategoryQueries.SettingKey, Value = category.Id.ToString() });
        await populated.SaveChangesAsync();
        await using var empty = await CreateTenantAsync();
        var emptyResult = await ReadAsync(empty);
        Assert.Equal(new AssociationReadinessFacts(), emptyResult.Facts);
        var populatedResult = await ReadAsync(populated);
        Assert.All(populatedResult.Checks.Where(x => x.Area != ReadinessArea.MembershipFeeRates), x => Assert.Equal(ReadinessStatus.Ready, x.Status));
        Assert.Equal(1, populatedResult.Facts.Members);
        Assert.Equal(1, populatedResult.Facts.Plots);
        Assert.Equal(1, populatedResult.Facts.CurrentOwnerships);
        empty.SystemSettings.Add(new() { Key = ElectricityExpenseCategoryQueries.SettingKey, Value = category.Id.ToString() });
        await empty.SaveChangesAsync();
        AssertCheck(await ReadAsync(empty), ReadinessArea.ElectricityExpenseCategory, ReadinessStatus.RequiredForOperation, ReadinessReason.CategoryUnavailable);
        Assert.Equal(emptyResult.Facts, (await ReadAsync(empty)).Facts);
    }

    [Fact]
    public async Task GetAsync_ExpiredAndFutureOwnerships_AreNotCurrent()
    {
        await using var db = await CreateTenantAsync();
        db.PlotOwnerships.Add(new() { Member = new() { FullName = "Past" }, Plot = new() { Number = "Past" }, ValidTo = Today.AddDays(-1) });
        db.PlotOwnerships.Add(new() { Member = new() { FullName = "Future" }, Plot = new() { Number = "Future" }, ValidFrom = Today.AddDays(1) });
        await db.SaveChangesAsync();
        var result = await ReadAsync(db);
        Assert.Equal(0, result.Facts.CurrentOwnerships);
        AssertCheck(result, ReadinessArea.MembersAndPlots, ReadinessStatus.RequiredForOperation, ReadinessReason.NoCurrentOwnership);
    }

    [Fact]
    public async Task GetAsync_UnresolvedContextOrCancellation_DoesNotReturnData()
    {
        await using var unresolved = fixture.CreateUnresolvedContext();
        await Assert.ThrowsAsync<AssociationIsolationException>(() => ReadAsync(unresolved));
        await using var db = await CreateTenantAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AssociationReadinessService(db, new FixedClock()).GetAsync(cancellation.Token));
    }

    private async Task<ApplicationDbContext> CreateTenantAsync()
    {
        await using var seed = fixture.CreateContext();
        var association = new Association { Slug = $"readiness-{Guid.NewGuid():N}", Name = "Readiness test", IsActive = true };
        seed.Add(association);
        await seed.SaveChangesAsync();
        return new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(seed.Database.GetConnectionString()).Options,
            TestAssociations.Resolved(association.Id, association.Slug));
    }

    private static MemberElectricityMeter Meter(MemberElectricityMeterType type = MemberElectricityMeterType.SingleRate, bool initial = false) => new()
    {
        Member = new() { FullName = "Readiness member", ElectricityMeterType = type },
        BillingPlot = new() { Number = Guid.NewGuid().ToString("N") },
        IsActive = true,
        Readings = initial ? [new() { ReadingDate = Today.AddDays(-1), IsInitialReading = true, CurrentNightReading = type == MemberElectricityMeterType.DayNight ? 0 : null }] : []
    };

    private static async Task<AssociationReadinessSnapshot> ReadAsync(ApplicationDbContext db)
    {
        db.ChangeTracker.Clear();
        var result = await new AssociationReadinessService(db, new FixedClock()).GetAsync();
        Assert.Empty(db.ChangeTracker.Entries());
        return result;
    }

    private static void AssertCheck(AssociationReadinessSnapshot result, ReadinessArea area, ReadinessStatus status, ReadinessReason reason) =>
        Assert.Equal(new ReadinessCheck(area, status, reason), Assert.Single(result.Checks, x => x.Area == area));

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
