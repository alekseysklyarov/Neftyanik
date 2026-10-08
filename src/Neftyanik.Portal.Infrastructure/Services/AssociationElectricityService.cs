using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class AssociationElectricityService : IAssociationElectricityService
{
    private const string ElectricitySupplierPayee = "Поставщик электроэнергии";
    private readonly ApplicationDbContext _dbContext;
    private readonly IFinancialAuditService _financialAuditService;

    public AssociationElectricityService(ApplicationDbContext dbContext)
        : this(dbContext, new FinancialAuditService(dbContext, new HttpContextAccessor()))
    {
    }

    public AssociationElectricityService(ApplicationDbContext dbContext, IFinancialAuditService financialAuditService)
    {
        _dbContext = dbContext;
        _financialAuditService = financialAuditService;
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

        var tariff = new AssociationElectricityTariff
        {
            EffectiveFrom = request.EffectiveFrom,
            DayRate = request.DayRate,
            NightRate = request.NightRate,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = request.CreatedByUserId
        };

        IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction is null)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            _dbContext.AssociationElectricityTariffs.Add(tariff);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _financialAuditService.Add(
                FinancialAuditLogActions.Created,
                nameof(AssociationElectricityTariff),
                tariff.Id.ToString(),
                "Изменены тарифы поставщика: день/ночь.",
                newValues: new
                {
                    TariffId = tariff.Id,
                    tariff.EffectiveFrom,
                    tariff.DayRate,
                    tariff.NightRate
                });

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ElectricityOperationResult.Success();
        }
        catch (DbUpdateException exception) when (exception.Message.Contains("AssociationElectricityTariffs", StringComparison.OrdinalIgnoreCase)
            || exception.InnerException?.Message.Contains("AssociationElectricityTariffs", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return ElectricityOperationResult.Failure("Тариф поставщика с такой датой уже существует.");
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
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
            return AssociationElectricityExpenseOperationResult.Failure("Не удалось определить пользователя.");
        if (request.PaymentMethod is not (Neftyanik.Portal.Domain.Enums.PaymentMethod.Cash or Neftyanik.Portal.Domain.Enums.PaymentMethod.BankTransfer))
            return AssociationElectricityExpenseOperationResult.Failure("Выберите кассу или банковский счёт.");
        if (request.DocumentNumber?.Length > 100)
            return AssociationElectricityExpenseOperationResult.Failure("Номер документа слишком длинный.");

        await using var transaction = _dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction is null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        await AdvancePaymentAllocator.LockAsync(_dbContext, cancellationToken);
        var reading = await _dbContext.AssociationElectricityReadings.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == request.ReadingId, cancellationToken);
        if (reading is null || reading.IsInitialReading || !reading.TotalSupplierAmount.HasValue)
            return AssociationElectricityExpenseOperationResult.Failure("Не найдено начисление поставщика.");
        var payments = await _dbContext.Expenses.AsNoTracking()
            .Where(e => e.AssociationElectricityReadingId == reading.Id && !e.IsCancelled)
            .Select(e => e.Amount).ToListAsync(cancellationToken);
        var remaining = reading.TotalSupplierAmount.Value - payments.Sum();
        var amount = request.Amount ?? remaining;
        var date = request.PaymentDate ?? DateOnly.FromDateTime(DateTime.Today);
        if (amount <= 0 || amount > remaining || decimal.Round(amount, 2) != amount)
            return AssociationElectricityExpenseOperationResult.Failure("Сумма должна быть больше нуля и не превышать остаток долга; укажите не более двух знаков после запятой.");
        if (date > DateOnly.FromDateTime(DateTime.Today))
            return AssociationElectricityExpenseOperationResult.Failure("Дата фактической оплаты не может быть в будущем.");
        var categoryId = await _dbContext.GetElectricityExpenseCategoryIdAsync(cancellationToken);
        if (!categoryId.HasValue)
            return AssociationElectricityExpenseOperationResult.Failure("Не настроен тип расхода на электроэнергию.");

        var expense = new Expense
        {
            ExpenseCategoryId = categoryId.Value, ExpenseDate = date, Amount = amount,
            FundingSource = 2, PaymentMethod = request.PaymentMethod, DocumentNumber = request.DocumentNumber?.Trim(),
            Description = $"Оплата поставщику по показаниям от {reading.ReadingDate:dd.MM.yyyy}.",
            Payee = ElectricitySupplierPayee, CreatedByUserId = request.CreatedByUserId,
            CreatedAt = DateTimeOffset.UtcNow, AssociationElectricityReadingId = reading.Id
        };
        _dbContext.Expenses.Add(expense);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _financialAuditService.Add(FinancialAuditLogActions.Created, nameof(Expense), expense.Id.ToString(),
            $"Зарегистрирована оплата поставщику #{expense.Id}.",
            newValues: new { expense.ExpenseDate, expense.Amount, expense.PaymentMethod, expense.DocumentNumber, expense.AssociationElectricityReadingId });
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return AssociationElectricityExpenseOperationResult.Success(expense.Id, expense.Amount);
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
        return await _dbContext.AssociationElectricityTariffs
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
