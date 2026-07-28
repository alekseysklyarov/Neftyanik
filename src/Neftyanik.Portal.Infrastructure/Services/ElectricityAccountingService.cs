using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class ElectricityAccountingService : IElectricityAccountingService
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private readonly ApplicationDbContext _dbContext;

    public ElectricityAccountingService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ElectricityOperationResult> CreateTariffAsync(CreateElectricityTariffRequest request, CancellationToken cancellationToken = default)
    {
        if (request.DayRate < 0m)
        {
            return ElectricityOperationResult.Failure("Дневной тариф не может быть отрицательным.");
        }

        if (request.NightRate < 0m)
        {
            return ElectricityOperationResult.Failure("Ночной тариф не может быть отрицательным.");
        }

        var exists = await _dbContext.ElectricityTariffs
            .AsNoTracking()
            .AnyAsync(tariff => tariff.EffectiveFrom == request.EffectiveFrom, cancellationToken);

        if (exists)
        {
            return ElectricityOperationResult.Failure("Тариф с такой датой начала действия уже существует.");
        }

        _dbContext.ElectricityTariffs.Add(new ElectricityTariff
        {
            EffectiveFrom = request.EffectiveFrom,
            DayRate = request.DayRate,
            NightRate = request.NightRate,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = request.CreatedByUserId
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ElectricityOperationResult.Success();
        }
        catch (DbUpdateException exception) when (IsTariffDuplicateViolation(exception))
        {
            return ElectricityOperationResult.Failure("Тариф с такой датой начала действия уже существует.");
        }
    }

    public async Task<ElectricityReadingOperationResult> CreateInitialReadingAsync(CreateInitialElectricityReadingRequest request, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateCurrentReadings(request.CurrentDayReading, request.CurrentNightReading);
        if (validationError is not null)
        {
            return ElectricityReadingOperationResult.Failure(validationError);
        }

        if (!await PlotExistsAsync(request.PlotId, cancellationToken))
        {
            return ElectricityReadingOperationResult.Failure("Участок не найден.");
        }

        var hasHistory = await _dbContext.ElectricityReadings
            .AsNoTracking()
            .AnyAsync(reading => reading.PlotId == request.PlotId, cancellationToken);

        if (hasHistory)
        {
            return ElectricityReadingOperationResult.Failure("Начальные показания можно внести только один раз.");
        }

        var reading = new ElectricityReading
        {
            PlotId = request.PlotId,
            ReadingDate = request.ReadingDate,
            CurrentDayReading = request.CurrentDayReading,
            CurrentNightReading = request.CurrentNightReading,
            IsInitialReading = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = request.CreatedByUserId
        };

        _dbContext.ElectricityReadings.Add(reading);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ElectricityReadingOperationResult.Success(reading.Id, null, null);
        }
        catch (DbUpdateException exception) when (IsReadingDuplicateViolation(exception))
        {
            return ElectricityReadingOperationResult.Failure("Показания на эту дату для участка уже существуют.");
        }
    }

    public async Task<ElectricityReadingOperationResult> CreateReadingAsync(CreateElectricityReadingRequest request, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateCurrentReadings(request.CurrentDayReading, request.CurrentNightReading);
        if (validationError is not null)
        {
            return ElectricityReadingOperationResult.Failure(validationError);
        }

        if (!await PlotExistsAsync(request.PlotId, cancellationToken))
        {
            return ElectricityReadingOperationResult.Failure("Участок не найден.");
        }

        var latestReading = await _dbContext.ElectricityReadings
            .AsNoTracking()
            .Where(reading => reading.PlotId == request.PlotId)
            .OrderByDescending(reading => reading.ReadingDate)
            .ThenByDescending(reading => reading.Id)
            .Select(reading => new LatestReadingSnapshot(
                reading.Id,
                reading.ReadingDate,
                reading.CurrentDayReading,
                reading.CurrentNightReading,
                reading.IsInitialReading))
            .FirstOrDefaultAsync(cancellationToken);

        if (latestReading is null)
        {
            return ElectricityReadingOperationResult.Failure("Сначала внесите начальные показания.");
        }

        if (request.ReadingDate <= latestReading.ReadingDate)
        {
            return ElectricityReadingOperationResult.Failure("Дата новых показаний должна быть позже последней сохраненной даты.");
        }

        if (request.CurrentDayReading < latestReading.CurrentDayReading)
        {
            return ElectricityReadingOperationResult.Failure("Текущее дневное показание не может быть меньше предыдущего.");
        }

        if (request.CurrentNightReading < latestReading.CurrentNightReading)
        {
            return ElectricityReadingOperationResult.Failure("Текущее ночное показание не может быть меньше предыдущего.");
        }

        var tariff = await _dbContext.ElectricityTariffs
            .AsNoTracking()
            .Where(item => item.EffectiveFrom <= request.ReadingDate)
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenByDescending(item => item.Id)
            .Select(item => new TariffSnapshot(item.Id, item.DayRate, item.NightRate, item.EffectiveFrom))
            .FirstOrDefaultAsync(cancellationToken);

        if (tariff is null)
        {
            return ElectricityReadingOperationResult.Failure("Для указанной даты показаний не найден действующий тариф.");
        }

        var dayConsumption = request.CurrentDayReading - latestReading.CurrentDayReading;
        var nightConsumption = request.CurrentNightReading - latestReading.CurrentNightReading;
        var dayAmount = RoundMoney(dayConsumption * tariff.DayRate);
        var nightAmount = RoundMoney(nightConsumption * tariff.NightRate);
        var totalAmount = dayAmount + nightAmount;
        var chargeType = await GetOrCreateElectricityChargeTypeAsync(cancellationToken);

        var charge = new Charge
        {
            PlotId = request.PlotId,
            ChargeType = chargeType,
            Amount = totalAmount,
            ChargeDate = request.ReadingDate,
            Description = BuildChargeDescription(request.ReadingDate, dayConsumption, nightConsumption),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = request.CreatedByUserId
        };

        var reading = new ElectricityReading
        {
            PlotId = request.PlotId,
            ReadingDate = request.ReadingDate,
            PreviousDayReading = latestReading.CurrentDayReading,
            CurrentDayReading = request.CurrentDayReading,
            DayConsumption = dayConsumption,
            DayRate = tariff.DayRate,
            DayAmount = dayAmount,
            PreviousNightReading = latestReading.CurrentNightReading,
            CurrentNightReading = request.CurrentNightReading,
            NightConsumption = nightConsumption,
            NightRate = tariff.NightRate,
            NightAmount = nightAmount,
            TotalAmount = totalAmount,
            IsInitialReading = false,
            Charge = charge,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = request.CreatedByUserId
        };

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        _dbContext.ElectricityReadings.Add(reading);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ElectricityReadingOperationResult.Success(reading.Id, reading.ChargeId, totalAmount);
        }
        catch (DbUpdateException exception) when (IsReadingDuplicateViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ElectricityReadingOperationResult.Failure("Показания на эту дату для участка уже существуют.");
        }
    }

    private async Task<bool> PlotExistsAsync(int plotId, CancellationToken cancellationToken)
    {
        return await _dbContext.Plots
            .AsNoTracking()
            .AnyAsync(plot => plot.Id == plotId, cancellationToken);
    }

    private async Task<ChargeType> GetOrCreateElectricityChargeTypeAsync(CancellationToken cancellationToken)
    {
        var existingChargeType = await _dbContext.ChargeTypes
            .FirstOrDefaultAsync(chargeType => chargeType.Code == ChargeTypeCodes.Electricity, cancellationToken);

        if (existingChargeType is not null)
        {
            return existingChargeType;
        }

        var chargeType = new ChargeType
        {
            Code = ChargeTypeCodes.Electricity,
            Name = "Электроэнергия",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.ChargeTypes.Add(chargeType);
        return chargeType;
    }

    private static string? ValidateCurrentReadings(decimal currentDayReading, decimal currentNightReading)
    {
        if (currentDayReading < 0m)
        {
            return "Дневное показание не может быть отрицательным.";
        }

        if (currentNightReading < 0m)
        {
            return "Ночное показание не может быть отрицательным.";
        }

        return null;
    }

    private static decimal RoundMoney(decimal amount)
    {
        return Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    private static string BuildChargeDescription(DateOnly readingDate, decimal dayConsumption, decimal nightConsumption)
    {
        return string.Format(
            RussianCulture,
            "Электроэнергия за {0:dd.MM.yyyy}: день {1:0.000} кВт·ч, ночь {2:0.000} кВт·ч",
            readingDate.ToDateTime(TimeOnly.MinValue),
            dayConsumption,
            nightConsumption);
    }

    private static bool IsReadingDuplicateViolation(DbUpdateException exception)
    {
        return exception.InnerException?.Message.Contains("ElectricityReadings", StringComparison.OrdinalIgnoreCase) == true
            || exception.Message.Contains("ElectricityReadings", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTariffDuplicateViolation(DbUpdateException exception)
    {
        return exception.InnerException?.Message.Contains("ElectricityTariffs", StringComparison.OrdinalIgnoreCase) == true
            || exception.Message.Contains("ElectricityTariffs", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record LatestReadingSnapshot(long Id, DateOnly ReadingDate, decimal CurrentDayReading, decimal CurrentNightReading, bool IsInitialReading);

    private sealed record TariffSnapshot(int Id, decimal DayRate, decimal NightRate, DateOnly EffectiveFrom);
}
