using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using System.Text.Json;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class AssociationElectricityService : IAssociationElectricityService
{
    private const string ElectricitySupplierPayee = "Поставщик электроэнергии";
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
        if (string.IsNullOrWhiteSpace(request.CreatedByUserId))
        {
            return ElectricityReadingOperationResult.Failure("Не удалось определить пользователя, который вносит расход по общему счетчику.");
        }

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

        var tariff = await GetApplicableSupplierTariffAsync(request.ReadingDate, cancellationToken);

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

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _dbContext.AssociationElectricityReadings.Add(reading);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return ElectricityReadingOperationResult.Success(reading.Id, null, totalAmount);
        }
        catch (DbUpdateException exception) when (exception.Message.Contains("AssociationElectricityReadings", StringComparison.OrdinalIgnoreCase)
            || exception.InnerException?.Message.Contains("AssociationElectricityReadings", StringComparison.OrdinalIgnoreCase) == true)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ElectricityReadingOperationResult.Failure("Показания общего счетчика на эту дату уже существуют.");
        }
    }

    public async Task<AssociationElectricityExpenseOperationResult> CreateExpenseAsync(CreateAssociationElectricityExpenseRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CreatedByUserId))
        {
            return AssociationElectricityExpenseOperationResult.Failure("Не удалось определить пользователя, который оплачивает электроэнергию по общему счетчику.");
        }

        var reading = await _dbContext.AssociationElectricityReadings
            .AsNoTracking()
            .Where(item => item.Id == request.ReadingId)
            .Select(item => new
            {
                item.Id,
                item.ReadingDate,
                item.PreviousDayReading,
                item.CurrentDayReading,
                item.DayConsumption,
                item.AppliedSupplierDayRate,
                item.PreviousNightReading,
                item.CurrentNightReading,
                item.NightConsumption,
                item.AppliedSupplierNightRate,
                item.TotalSupplierAmount,
                item.IsInitialReading,
                HasExpense = item.SupplierExpense != null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (reading is null)
        {
            return AssociationElectricityExpenseOperationResult.Failure("Показания общего счетчика не найдены.");
        }

        if (reading.IsInitialReading)
        {
            return AssociationElectricityExpenseOperationResult.Failure("Для начальных показаний расход не создается.");
        }

        if (reading.HasExpense)
        {
            return AssociationElectricityExpenseOperationResult.Failure("Расход по этим показаниям уже создан.");
        }

        if (!reading.DayConsumption.HasValue
            || !reading.NightConsumption.HasValue
            || !reading.AppliedSupplierDayRate.HasValue
            || !reading.AppliedSupplierNightRate.HasValue
            || !reading.TotalSupplierAmount.HasValue
            || !reading.PreviousDayReading.HasValue
            || !reading.PreviousNightReading.HasValue)
        {
            return AssociationElectricityExpenseOperationResult.Failure("Недостаточно данных для создания расхода по указанным показаниям.");
        }

        var expense = new Expense
        {
            ExpenseCategoryId = ExpenseCategoryIds.ElectricityPayment,
            ExpenseDate = reading.ReadingDate,
            Amount = reading.TotalSupplierAmount.Value,
            Description = BuildElectricityExpenseDescription(
                reading.PreviousDayReading.Value,
                reading.CurrentDayReading,
                reading.DayConsumption.Value,
                reading.AppliedSupplierDayRate.Value,
                reading.PreviousNightReading.Value,
                reading.CurrentNightReading,
                reading.NightConsumption.Value,
                reading.AppliedSupplierNightRate.Value),
            Payee = ElectricitySupplierPayee,
            CreatedByUserId = request.CreatedByUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            AssociationElectricityReadingId = reading.Id
        };

        _dbContext.Expenses.Add(expense);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            _dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = request.CreatedByUserId,
                Action = "Create",
                EntityType = nameof(Expense),
                EntityId = expense.Id.ToString(),
                NewValues = JsonSerializer.Serialize(new
                {
                    expense.ExpenseCategoryId,
                    expense.ExpenseDate,
                    expense.Amount,
                    expense.Description,
                    expense.Payee,
                    expense.AssociationElectricityReadingId
                }),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await _dbContext.SaveChangesAsync(cancellationToken);

            return AssociationElectricityExpenseOperationResult.Success(expense.Id, expense.Amount);
        }
        catch (DbUpdateException exception) when (exception.Message.Contains("AssociationElectricityReadingId", StringComparison.OrdinalIgnoreCase)
            || exception.InnerException?.Message.Contains("AssociationElectricityReadingId", StringComparison.OrdinalIgnoreCase) == true)
        {
            return AssociationElectricityExpenseOperationResult.Failure("Расход по этим показаниям уже создан.");
        }
    }

    private static string? ValidateReadings(decimal currentDayReading, decimal currentNightReading)
    {
        if (currentDayReading < 0m)
        {
            return "Дневное показание не может быть отрицательным.";
        }

        if (decimal.Truncate(currentDayReading) != currentDayReading)
        {
            return "Дневное показание должно быть целым числом.";
        }

        if (currentNightReading < 0m)
        {
            return "Ночное показание не может быть отрицательным.";
        }

        if (decimal.Truncate(currentNightReading) != currentNightReading)
        {
            return "Ночное показание должно быть целым числом.";
        }

        return null;
    }

    private async Task<SupplierTariffSnapshot?> GetApplicableSupplierTariffAsync(DateOnly readingDate, CancellationToken cancellationToken)
    {
        var associationTariff = await _dbContext.AssociationElectricityTariffs
            .AsNoTracking()
            .Where(item => item.EffectiveFrom <= readingDate)
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenByDescending(item => item.Id)
            .Select(item => new SupplierTariffSnapshot(item.DayRate, item.NightRate))
            .FirstOrDefaultAsync(cancellationToken);

        if (associationTariff is not null)
        {
            return associationTariff;
        }

        return await _dbContext.ElectricityTariffs
            .AsNoTracking()
            .Where(item => item.EffectiveFrom <= readingDate)
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenByDescending(item => item.Id)
            .Select(item => new SupplierTariffSnapshot(item.DayRate, item.NightRate))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static decimal RoundMoney(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private sealed record SupplierTariffSnapshot(decimal DayRate, decimal NightRate);

    private static string BuildElectricityExpenseDescription(
        decimal previousDayReading,
        decimal currentDayReading,
        decimal dayConsumption,
        decimal dayRate,
        decimal previousNightReading,
        decimal currentNightReading,
        decimal nightConsumption,
        decimal nightRate)
    {
        return $"Общий счетчик: Т1 {previousDayReading:0.000} → {currentDayReading:0.000}, расход {dayConsumption:0.000} кВт·ч × {dayRate:0.0000} грн; "
            + $"Т2 {previousNightReading:0.000} → {currentNightReading:0.000}, расход {nightConsumption:0.000} кВт·ч × {nightRate:0.0000} грн.";
    }
}
