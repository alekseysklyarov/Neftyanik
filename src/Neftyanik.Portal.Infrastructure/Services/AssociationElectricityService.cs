using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class AssociationElectricityService : IAssociationElectricityService
{
    private readonly ApplicationDbContext _dbContext;

    public AssociationElectricityService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ElectricityOperationResult> CreateTariffAsync(CreateAssociationElectricityTariffRequest request, CancellationToken cancellationToken = default)
    {
        if (request.DayRate < 0m)
        {
            return ElectricityOperationResult.Failure("Дневной тариф поставщика не может быть отрицательным.");
        }

        if (request.NightRate < 0m)
        {
            return ElectricityOperationResult.Failure("Ночной тариф поставщика не может быть отрицательным.");
        }

        var exists = await _dbContext.AssociationElectricityTariffs
            .AsNoTracking()
            .AnyAsync(tariff => tariff.EffectiveFrom == request.EffectiveFrom, cancellationToken);

        if (exists)
        {
            return ElectricityOperationResult.Failure("Тариф поставщика с такой датой уже существует.");
        }

        _dbContext.AssociationElectricityTariffs.Add(new AssociationElectricityTariff
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
        catch (DbUpdateException exception) when (exception.Message.Contains("AssociationElectricityTariffs", StringComparison.OrdinalIgnoreCase)
            || exception.InnerException?.Message.Contains("AssociationElectricityTariffs", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ElectricityOperationResult.Failure("Тариф поставщика с такой датой уже существует.");
        }
    }

    public async Task<ElectricityReadingOperationResult> CreateInitialReadingAsync(CreateAssociationElectricityInitialReadingRequest request, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateReadings(request.CurrentDayReading, request.CurrentNightReading);
        if (validationError is not null)
        {
            return ElectricityReadingOperationResult.Failure(validationError);
        }

        var hasHistory = await _dbContext.AssociationElectricityReadings
            .AsNoTracking()
            .AnyAsync(cancellationToken);

        if (hasHistory)
        {
            return ElectricityReadingOperationResult.Failure("Начальные показания общего счетчика можно внести только один раз.");
        }

        var reading = new AssociationElectricityReading
        {
            ReadingDate = request.ReadingDate,
            CurrentDayReading = request.CurrentDayReading,
            CurrentNightReading = request.CurrentNightReading,
            IsInitialReading = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = request.CreatedByUserId
        };

        _dbContext.AssociationElectricityReadings.Add(reading);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ElectricityReadingOperationResult.Success(reading.Id, null, null);
        }
        catch (DbUpdateException exception) when (exception.Message.Contains("AssociationElectricityReadings", StringComparison.OrdinalIgnoreCase)
            || exception.InnerException?.Message.Contains("AssociationElectricityReadings", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ElectricityReadingOperationResult.Failure("Показания общего счетчика на эту дату уже существуют.");
        }
    }

    public async Task<ElectricityReadingOperationResult> CreateReadingAsync(CreateAssociationElectricityReadingRequest request, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateReadings(request.CurrentDayReading, request.CurrentNightReading);
        if (validationError is not null)
        {
            return ElectricityReadingOperationResult.Failure(validationError);
        }

        var latestReading = await _dbContext.AssociationElectricityReadings
            .AsNoTracking()
            .OrderByDescending(reading => reading.ReadingDate)
            .ThenByDescending(reading => reading.Id)
            .Select(reading => new
            {
                reading.ReadingDate,
                reading.CurrentDayReading,
                reading.CurrentNightReading
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (latestReading is null)
        {
            return ElectricityReadingOperationResult.Failure("Сначала внесите начальные показания общего счетчика.");
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

        var tariff = await _dbContext.AssociationElectricityTariffs
            .AsNoTracking()
            .Where(item => item.EffectiveFrom <= request.ReadingDate)
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenByDescending(item => item.Id)
            .Select(item => new
            {
                item.DayRate,
                item.NightRate
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (tariff is null)
        {
            return ElectricityReadingOperationResult.Failure("Для указанной даты не найден тариф поставщика.");
        }

        var dayConsumption = request.CurrentDayReading - latestReading.CurrentDayReading;
        var nightConsumption = request.CurrentNightReading - latestReading.CurrentNightReading;
        var dayAmount = RoundMoney(dayConsumption * tariff.DayRate);
        var nightAmount = RoundMoney(nightConsumption * tariff.NightRate);
        var totalConsumption = dayConsumption + nightConsumption;
        var totalAmount = dayAmount + nightAmount;

        var reading = new AssociationElectricityReading
        {
            ReadingDate = request.ReadingDate,
            PreviousDayReading = latestReading.CurrentDayReading,
            CurrentDayReading = request.CurrentDayReading,
            DayConsumption = dayConsumption,
            AppliedSupplierDayRate = tariff.DayRate,
            DayAmount = dayAmount,
            PreviousNightReading = latestReading.CurrentNightReading,
            CurrentNightReading = request.CurrentNightReading,
            NightConsumption = nightConsumption,
            AppliedSupplierNightRate = tariff.NightRate,
            NightAmount = nightAmount,
            TotalConsumption = totalConsumption,
            TotalSupplierAmount = totalAmount,
            IsInitialReading = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = request.CreatedByUserId
        };

        _dbContext.AssociationElectricityReadings.Add(reading);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ElectricityReadingOperationResult.Success(reading.Id, null, totalAmount);
        }
        catch (DbUpdateException exception) when (exception.Message.Contains("AssociationElectricityReadings", StringComparison.OrdinalIgnoreCase)
            || exception.InnerException?.Message.Contains("AssociationElectricityReadings", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ElectricityReadingOperationResult.Failure("Показания общего счетчика на эту дату уже существуют.");
        }
    }

    private static string? ValidateReadings(decimal currentDayReading, decimal currentNightReading)
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

    private static decimal RoundMoney(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
