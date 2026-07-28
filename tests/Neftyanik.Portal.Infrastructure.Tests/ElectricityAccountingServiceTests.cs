using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class ElectricityAccountingServiceTests
{
    [Fact]
    public async Task CreateReadingAsync_UsesLatestTariffEffectiveOnReadingDate()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 100m, 50m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.10m, 2.05m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 8, 1), 4.32m, 2.16m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 8, 15), 120m, 60m, null));

        Assert.True(result.Succeeded);
        var reading = await fixture.Context.ElectricityReadings.SingleAsync(x => x.Id == result.ReadingId);
        Assert.Equal(4.32m, reading.DayRate);
        Assert.Equal(2.16m, reading.NightRate);
    }

    [Fact]
    public async Task CreateReadingAsync_IgnoresFutureTariff()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 100m, 50m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.10m, 2.05m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 9, 1), 4.32m, 2.16m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 8, 15), 120m, 60m, null));

        Assert.True(result.Succeeded);
        var reading = await fixture.Context.ElectricityReadings.SingleAsync(x => x.Id == result.ReadingId);
        Assert.Equal(4.10m, reading.DayRate);
        Assert.Equal(2.05m, reading.NightRate);
    }

    [Fact]
    public async Task CreateReadingAsync_WithoutApplicableTariff_ReturnsValidationFailure()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 100m, 50m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 8, 15), 120m, 60m, null));

        Assert.False(result.Succeeded);
        Assert.Equal("Для указанной даты показаний не найден действующий тариф.", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateInitialReadingAsync_SavesFirstReadingWithoutCharge()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);

        var result = await fixture.Service.CreateInitialReadingAsync(new CreateInitialElectricityReadingRequest(1, new DateOnly(2026, 7, 1), 12320.4m, 6180.1m, null));

        Assert.True(result.Succeeded);
        var reading = await fixture.Context.ElectricityReadings.SingleAsync();
        Assert.True(reading.IsInitialReading);
        Assert.Null(reading.ChargeId);
        Assert.Null(reading.TotalAmount);
    }

    [Fact]
    public async Task CreateInitialReadingAsync_RejectsSecondInitialReading()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 1m, 1m);

        var result = await fixture.Service.CreateInitialReadingAsync(new CreateInitialElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 2m, 2m, null));

        Assert.False(result.Succeeded);
        Assert.Equal("Начальные показания можно внести только один раз.", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateInitialReadingAsync_AllowsZeroValues()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);

        var result = await fixture.Service.CreateInitialReadingAsync(new CreateInitialElectricityReadingRequest(1, new DateOnly(2026, 7, 1), 0m, 0m, null));

        Assert.True(result.Succeeded);
        var reading = await fixture.Context.ElectricityReadings.SingleAsync();
        Assert.Equal(0m, reading.CurrentDayReading);
        Assert.Equal(0m, reading.CurrentNightReading);
    }

    [Fact]
    public async Task CreateReadingAsync_CalculatesDayAndNightConsumptionCorrectly()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 12320.4m, 6180.1m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.32m, 2.16m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 31), 12450.7m, 6230.2m, null));

        Assert.True(result.Succeeded);
        var reading = await fixture.Context.ElectricityReadings.SingleAsync(x => x.Id == result.ReadingId);
        Assert.Equal(130.3m, reading.DayConsumption);
        Assert.Equal(50.1m, reading.NightConsumption);
    }

    [Fact]
    public async Task CreateReadingAsync_AllowsUnchangedValuesAndProducesZeroConsumption()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 100m, 100m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.32m, 2.16m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 100m, 100m, null));

        Assert.True(result.Succeeded);
        var reading = await fixture.Context.ElectricityReadings.SingleAsync(x => x.Id == result.ReadingId);
        Assert.Equal(0m, reading.DayConsumption);
        Assert.Equal(0m, reading.NightConsumption);
        Assert.Equal(0m, reading.TotalAmount);
    }

    [Fact]
    public async Task CreateReadingAsync_RejectsLowerDayReading()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 100m, 100m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.32m, 2.16m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 99.999m, 100m, null));

        Assert.False(result.Succeeded);
        Assert.Equal("Текущее дневное показание не может быть меньше предыдущего.", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateReadingAsync_RejectsLowerNightReading()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 100m, 100m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.32m, 2.16m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 100m, 99.999m, null));

        Assert.False(result.Succeeded);
        Assert.Equal("Текущее ночное показание не может быть меньше предыдущего.", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateReadingAsync_CopiesTariffValuesIntoReading()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 100m, 50m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.3255m, 2.1655m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 105m, 55m, null));

        Assert.True(result.Succeeded);
        var reading = await fixture.Context.ElectricityReadings.SingleAsync(x => x.Id == result.ReadingId);
        Assert.Equal(4.3255m, reading.DayRate);
        Assert.Equal(2.1655m, reading.NightRate);
    }

    [Fact]
    public async Task CreateReadingAsync_CalculatesRoundedTotalAmount()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 1000m, 2000m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 1.0050m, 2.0050m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 1001.005m, 2001.005m, null));

        Assert.True(result.Succeeded);
        var reading = await fixture.Context.ElectricityReadings.SingleAsync(x => x.Id == result.ReadingId);
        Assert.Equal(1.01m, reading.DayAmount);
        Assert.Equal(2.02m, reading.NightAmount);
        Assert.Equal(3.03m, reading.TotalAmount);
    }

    [Fact]
    public async Task CreateReadingAsync_CreatesExactlyOneCharge()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 100m, 50m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.32m, 2.16m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 120m, 60m, null));

        Assert.True(result.Succeeded);
        Assert.Equal(1, await fixture.Context.Charges.CountAsync());
    }

    [Fact]
    public async Task CreateReadingAsync_CreatesChargeWithAmountEqualToReadingTotal()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 100m, 50m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.32m, 2.16m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 120m, 60m, null));

        Assert.True(result.Succeeded);
        var reading = await fixture.Context.ElectricityReadings.SingleAsync(x => x.Id == result.ReadingId);
        var charge = await fixture.Context.Charges.SingleAsync();
        Assert.Equal(reading.TotalAmount, charge.Amount);
    }

    [Fact]
    public async Task CreateReadingAsync_LinksReadingToCreatedCharge()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 100m, 50m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.32m, 2.16m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 120m, 60m, null));

        Assert.True(result.Succeeded);
        var reading = await fixture.Context.ElectricityReadings.SingleAsync(x => x.Id == result.ReadingId);
        Assert.NotNull(reading.ChargeId);
        Assert.Equal(result.ChargeId, reading.ChargeId);
    }

    [Fact]
    public async Task CreateReadingAsync_CreatesChargeForSamePlot()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 100m, 50m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.32m, 2.16m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 120m, 60m, null));

        Assert.True(result.Succeeded);
        var charge = await fixture.Context.Charges.SingleAsync();
        Assert.Equal(1, charge.PlotId);
    }

    [Fact]
    public async Task CreateReadingAsync_DoesNotCreateDuplicateChargeForSameReadingDate()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 100m, 50m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.32m, 2.16m);
        var first = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 120m, 60m, null));

        var second = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 125m, 65m, null));

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal(1, await fixture.Context.Charges.CountAsync());
    }

    [Fact]
    public async Task CreateReadingAsync_RejectsEarlierReadingDate()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 5), 100m, 50m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.32m, 2.16m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 4), 120m, 60m, null));

        Assert.False(result.Succeeded);
        Assert.Equal("Дата новых показаний должна быть позже последней сохраненной даты.", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateReadingAsync_RejectsDuplicateDateForSamePlot()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 100m, 50m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.32m, 2.16m);
        await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 120m, 60m, null));

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 130m, 70m, null));

        Assert.False(result.Succeeded);
        Assert.Equal("Дата новых показаний должна быть позже последней сохраненной даты.", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateInitialReadingAsync_AllowsSameDateOnDifferentPlots()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        SeedPlot(fixture.Context, 2);

        var first = await fixture.Service.CreateInitialReadingAsync(new CreateInitialElectricityReadingRequest(1, new DateOnly(2026, 7, 1), 10m, 10m, null));
        var second = await fixture.Service.CreateInitialReadingAsync(new CreateInitialElectricityReadingRequest(2, new DateOnly(2026, 7, 1), 20m, 20m, null));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(2, await fixture.Context.ElectricityReadings.CountAsync());
    }

    [Fact]
    public async Task CreateReadingAsync_CreatesElectricityChargeTypeWithStableCode()
    {
        await using var fixture = await CreateFixtureAsync();
        SeedPlot(fixture.Context, 1);
        await CreateInitialReadingAsync(fixture.Service, 1, new DateOnly(2026, 7, 1), 100m, 50m);
        await CreateTariffAsync(fixture.Service, new DateOnly(2026, 7, 1), 4.32m, 2.16m);

        var result = await fixture.Service.CreateReadingAsync(new CreateElectricityReadingRequest(1, new DateOnly(2026, 7, 2), 120m, 60m, null));

        Assert.True(result.Succeeded);
        var chargeType = await fixture.Context.ChargeTypes.SingleAsync();
        Assert.Equal(ChargeTypeCodes.Electricity, chargeType.Code);
        Assert.Equal("Электроэнергия", chargeType.Name);
    }

    private static void SeedPlot(ApplicationDbContext context, int plotId)
    {
        context.Plots.Add(new Plot
        {
            Id = plotId,
            Number = $"P-{plotId}",
            IsActive = true
        });

        context.SaveChanges();
    }

    private static async Task CreateTariffAsync(ElectricityAccountingService service, DateOnly effectiveFrom, decimal dayRate, decimal nightRate)
    {
        var result = await service.CreateTariffAsync(new CreateElectricityTariffRequest(effectiveFrom, dayRate, nightRate, null));
        Assert.True(result.Succeeded);
    }

    private static async Task CreateInitialReadingAsync(ElectricityAccountingService service, int plotId, DateOnly readingDate, decimal day, decimal night)
    {
        var result = await service.CreateInitialReadingAsync(new CreateInitialElectricityReadingRequest(plotId, readingDate, day, night, null));
        Assert.True(result.Succeeded);
    }

    private static async Task<TestFixture> CreateFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        return new TestFixture(context, new ElectricityAccountingService(context));
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        public TestFixture(ApplicationDbContext context, ElectricityAccountingService service)
        {
            Context = context;
            Service = service;
        }

        public ApplicationDbContext Context { get; }

        public ElectricityAccountingService Service { get; }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
        }
    }
}
