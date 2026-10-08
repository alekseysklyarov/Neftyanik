using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public sealed class TwoPhysicalMeterReadingTests
{
    private static readonly DateOnly Date = new(2026, 9, 21);
    private static MemberElectricityService Service(ApplicationDbContext db) => new(db, new FinancialAuditService(db, new HttpContextAccessor()));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoMeters_PersistBreakdownAndCreateOneChargeUsingSummedReadings(bool dayNight)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db, dayNight);
            var result = await Service(db).CreateReadingAsync(new(901, Date, 70.125m, dayNight ? 40m : null, null,
                SecondMeterReading: 80.875m, SecondMeterNightReading: dayNight ? 30m : null));
            Assert.True(result.Succeeded, result.ErrorMessage);
            db.ChangeTracker.Clear();
            var reading = await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading);
            Assert.Equal(151m, reading.CurrentReading);
            Assert.Equal(dayNight ? 70m : (decimal?)null, reading.CurrentNightReading);
            Assert.Equal(new PhysicalMeterReadings(70.125m, 80.875m, dayNight ? 40m : null, dayNight ? 30m : null),
                PhysicalMeterReadings.FromJson(reading.PhysicalMeterReadingsJson));
            Assert.Equal(dayNight ? 295m : 255m, (await db.Charges.SingleAsync()).Amount);
            Assert.Equal(1, await db.MemberElectricityMeters.CountAsync());
            Assert.Equal(2, await db.MemberElectricityReadings.CountAsync());
            Assert.True(await db.FinancialAuditLogs.AnyAsync(a => a.EntityType == nameof(MemberElectricityReading)
                && a.NewValuesJson!.Contains("PhysicalMeterReadingsJson")));
        });
    }

    [Theory]
    [InlineData(-1, 150)]
    [InlineData(150, -1)]
    [InlineData(40, 50)]
    [InlineData(60.0001, 80)]
    [InlineData(999999999999999, 1000)]
    public async Task InvalidPair_DoesNotPersistReadingOrCharge(decimal first, decimal second)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            var result = await Service(db).CreateReadingAsync(new(901, Date, first, null, null, SecondMeterReading: second));
            Assert.False(result.Succeeded);
            Assert.Equal(1, await db.MemberElectricityReadings.CountAsync());
            Assert.Empty(await db.Charges.ToListAsync());
        });
    }

    [Fact]
    public async Task DayNight_RequiresBothZonesOfBothMeters()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db, true);
            foreach (var values in new (decimal? first, decimal? second)[] { (40, null), (null, 30), (null, null) })
            {
                var result = await Service(db).CreateReadingAsync(new(901, Date, 70, values.first, null,
                    SecondMeterReading: 80, SecondMeterNightReading: values.second));
                Assert.False(result.Succeeded);
            }
            Assert.Empty(await db.Charges.ToListAsync());
        });
    }

    [Fact]
    public async Task LaterPair_CannotHideDecreaseOfOneMeter_OrOmitSecondMeter()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            var service = Service(db);
            Assert.True((await service.CreateReadingAsync(new(901, Date, 70, null, null, true, 80))).Succeeded);
            Assert.False((await service.CreateReadingAsync(new(901, Date.AddDays(1), 60, null, null, true, 100))).Succeeded);
            Assert.False((await service.CreateReadingAsync(new(901, Date.AddDays(1), 160, null, null, true))).Succeeded);
            Assert.Equal(1, await db.Charges.CountAsync());
            Assert.True((await service.CreateReadingAsync(new(901, Date.AddDays(1), 75, null, null, true, 85))).Succeeded);
            Assert.Equal(50m, (await db.Charges.OrderByDescending(c => c.Id).FirstAsync()).Amount);
        });
    }

    [Fact]
    public async Task SingleMeter_KeepsExistingStorageAndCalculation()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db, twoMeters: false);
            Assert.True((await Service(db).CreateReadingAsync(new(901, Date, 140, null, null, true))).Succeeded);
            Assert.Null((await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading)).PhysicalMeterReadingsJson);
            Assert.Equal(200m, (await db.Charges.SingleAsync()).Amount);
        });
    }

    [Fact]
    public async Task MemberForm_PostsTwoReadings_AndShowsBreakdownInHistory()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("two-meters-member", RoleNames.Member), cultureName: "ru-RU");
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            (await db.Members.SingleAsync()).ApplicationUserId = "two-meters-member";
            await db.SaveChangesAsync();
        });
        const string url = "/neftyanik/Member/Electricity/Meters/901/Readings/Create";
        var html = await (await client.GetAsync(url)).ReadDecodedHtmlAsync();
        Assert.Contains("Input.SecondMeterReading", html);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var response = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
            ["Input.ReadingDate"] = "2026-09-21", ["Input.CurrentReading"] = "70", ["Input.SecondMeterReading"] = "80"
        }));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var history = await (await client.GetAsync(response.Headers.Location)).ReadDecodedHtmlAsync();
        Assert.Contains("Счётчик 1", history);
        Assert.Contains("Счётчик 2", history);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var reading = await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading);
            Assert.Equal(150m, reading.CurrentReading);
            Assert.True(reading.SubmittedByMember);
            Assert.Equal("two-meters-member", reading.CreatedByUserId);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnablingTwoMeters_PreservesHistoryAndBillsOnlyIncreaseFromPreviousTotal(bool dayNight)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db, dayNight, twoMeters: false);
            var service = Service(db);
            Assert.True((await service.CreateReadingAsync(new(901, Date, 150, dayNight ? 70m : null, null, true))).Succeeded);
            var oldReading = await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading);
            var oldCharge = await db.Charges.SingleAsync();
            var oldChargeAmount = oldCharge.Amount;
            var oldChargeId = oldCharge.Id;

            (await db.Members.SingleAsync()).HasTwoElectricityMeters = true;
            await db.SaveChangesAsync();
            var result = await service.CreateReadingAsync(new(901, Date.AddDays(1), 75, dayNight ? 45m : null, null, true,
                SecondMeterReading: 85, SecondMeterNightReading: dayNight ? 35m : null));
            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(dayNight ? 70m : 50m, result.TotalAmount);
            db.ChangeTracker.Clear();
            var previous = await db.MemberElectricityReadings.SingleAsync(r => r.Id == oldReading.Id);
            Assert.Equal(150m, previous.CurrentReading);
            Assert.Equal(dayNight ? 70m : (decimal?)null, previous.CurrentNightReading);
            Assert.Null(previous.PhysicalMeterReadingsJson);
            Assert.Equal(oldChargeAmount, (await db.Charges.SingleAsync(c => c.Id == oldChargeId)).Amount);
            Assert.Equal(2, await db.Charges.CountAsync());
            var next = await service.CreateReadingAsync(new(901, Date.AddDays(2), 80, dayNight ? 50m : null, null, true,
                SecondMeterReading: 90, SecondMeterNightReading: dayNight ? 40m : null));
            Assert.True(next.Succeeded, next.ErrorMessage);
            Assert.Equal(dayNight ? 70m : 50m, next.TotalAmount);

            // Returning to the legacy entry form also continues on the same total scale.
            (await db.Members.SingleAsync()).HasTwoElectricityMeters = false;
            await db.SaveChangesAsync();
            var legacy = await service.CreateReadingAsync(new(901, Date.AddDays(3), 180, dayNight ? 100m : null, null, true));
            Assert.True(legacy.Succeeded, legacy.ErrorMessage);
            Assert.Equal(dayNight ? 70m : 50m, legacy.TotalAmount);
        });
    }

    [Fact]
    public async Task DisabledSetting_HidesSecondInputAndRejectsForgedPair()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("single-meter-member", RoleNames.Member), cultureName: "ru-RU");
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db, twoMeters: false);
            (await db.Members.SingleAsync()).ApplicationUserId = "single-meter-member";
            await db.SaveChangesAsync();
            var result = await Service(db).CreateReadingAsync(new(901, Date, 70, null, null, true, 80));
            Assert.False(result.Succeeded);
            Assert.Empty(await db.Charges.ToListAsync());
        });
        var html = await (await client.GetAsync("/neftyanik/Member/Electricity/Meters/901/Readings/Create")).ReadDecodedHtmlAsync();
        Assert.DoesNotContain("Input.SecondMeterReading", html);
    }

    [Fact]
    public async Task Administrator_CanEnableSettingWithoutChangingReadingsOrCharges()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("two-meter-settings-admin", RoleNames.Administrator), cultureName: "ru-RU");
        await factory.ExecuteDbContextAsync(async db => await SeedAsync(db, twoMeters: false));
        const string url = "/neftyanik/Administration/Members/Edit/701";
        var html = await (await client.GetAsync(url)).ReadDecodedHtmlAsync();
        Assert.Contains("Input.HasTwoElectricityMeters", html);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var response = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
            ["Input.FullName"] = "Two Meter Member", ["Input.HasTwoElectricityMeters"] = "true", ["Input.IsActive"] = "true"
        }));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.True((await db.Members.SingleAsync()).HasTwoElectricityMeters);
            Assert.Equal(100m, (await db.MemberElectricityReadings.SingleAsync()).CurrentReading);
            Assert.Empty(await db.Charges.ToListAsync());
        });
        var readingHtml = await (await client.GetAsync("/neftyanik/Administration/Electricity/Meters/901/Readings/Create")).ReadDecodedHtmlAsync();
        Assert.Contains("Input.SecondMeterReading", readingHtml);
    }

    [Fact]
    public async Task CorrectionOfTotal_ClearsObsoleteBreakdownButPreservesItInAudit()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("two-meters-admin", RoleNames.Administrator));
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            Assert.True((await Service(db).CreateReadingAsync(new(901, Date, 70, null, null, SecondMeterReading: 80))).Succeeded);
            var reading = await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading);
            var result = await new MemberReadingCorrectionService(db, new FinancialAuditService(db, new HttpContextAccessor()))
                .CorrectAsync(901, reading.Id, 140, null, "Исправление суммы", "two-meters-admin");
            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Null(reading.PhysicalMeterReadingsJson);
            Assert.True(await db.FinancialAuditLogs.AnyAsync(a => a.EntityType == nameof(MemberElectricityReading)
                && a.Action == FinancialAuditLogActions.Updated && a.OldValuesJson!.Contains("First")));
        });
    }

    private static async Task SeedAsync(ApplicationDbContext db, bool dayNight = false, bool twoMeters = true)
    {
        db.Members.Add(new Member { Id = 701, FullName = "Two Meter Member", IsActive = true, HasTwoElectricityMeters = twoMeters,
            ElectricityMeterType = dayNight ? MemberElectricityMeterType.DayNight : MemberElectricityMeterType.SingleRate });
        db.Plots.Add(new Plot { Id = 801, Number = "T-801", IsActive = true });
        db.PlotOwnerships.Add(new PlotOwnership { MemberId = 701, PlotId = 801, ValidFrom = new(2020, 1, 1) });
        db.MemberElectricityMeters.Add(new MemberElectricityMeter { Id = 901, MemberId = 701, BillingPlotId = 801, IsActive = true,
            Readings = [new MemberElectricityReading { ReadingDate = new(2026, 9, 1), CurrentReading = 100,
                CurrentNightReading = dayNight ? 50m : null, IsInitialReading = true }] });
        db.MemberElectricityTariffs.Add(new MemberElectricityTariff { EffectiveFrom = new(2020, 1, 1), Rate = 5m, NightRate = dayNight ? 2m : null });
        await db.SaveChangesAsync();
        (await db.Plots.SingleAsync()).MemberElectricityMeterId = 901;
        await db.SaveChangesAsync();
    }
}
