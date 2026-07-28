using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using System.Globalization;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class MemberElectricityService : IMemberElectricityService
{
    private const decimal MaxReadingIncrease = 500m;
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private readonly ApplicationDbContext _dbContext;

    public MemberElectricityService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ElectricityOperationResult> CreateTariffAsync(CreateMemberElectricityTariffRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Rate < 0m)
        {
            return ElectricityOperationResult.Failure("Тариф для участников не может быть отрицательным.");
        }

        var exists = await _dbContext.MemberElectricityTariffs
            .AsNoTracking()
            .AnyAsync(tariff => tariff.EffectiveFrom == request.EffectiveFrom, cancellationToken);

        if (exists)
        {
            return ElectricityOperationResult.Failure("Тариф для участников с такой датой уже существует.");
        }

        _dbContext.MemberElectricityTariffs.Add(new MemberElectricityTariff
        {
            EffectiveFrom = request.EffectiveFrom,
            Rate = request.Rate,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = request.CreatedByUserId
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ElectricityOperationResult.Success();
        }
        catch (DbUpdateException exception) when (exception.Message.Contains("MemberElectricityTariffs", StringComparison.OrdinalIgnoreCase)
            || exception.InnerException?.Message.Contains("MemberElectricityTariffs", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ElectricityOperationResult.Failure("Тариф для участников с такой датой уже существует.");
        }
    }

    public async Task<ElectricityReadingOperationResult> CreateInitialReadingWithDebtAsync(CreateMemberElectricityInitializationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.CurrentReading < 0m)
        {
            return ElectricityReadingOperationResult.Failure("Показание не может быть отрицательным.");
        }

        if (request.OpeningDebtAmount < 0m)
        {
            return ElectricityReadingOperationResult.Failure("Начальная задолженность не может быть отрицательной.");
        }

        var meter = await _dbContext.MemberElectricityMeters
            .Include(item => item.BillingPlot)
            .FirstOrDefaultAsync(item => item.Id == request.MeterId, cancellationToken);

        if (meter is null)
        {
            return ElectricityReadingOperationResult.Failure("Счетчик не найден.");
        }

        var hasHistory = await _dbContext.MemberElectricityReadings
            .AsNoTracking()
            .AnyAsync(reading => reading.MemberElectricityMeterId == request.MeterId, cancellationToken);

        if (hasHistory)
        {
            return ElectricityReadingOperationResult.Failure("Начальные показания можно внести только один раз.");
        }

        var reading = new MemberElectricityReading
        {
            MemberElectricityMeterId = request.MeterId,
            ReadingDate = request.ReadingDate,
            CurrentReading = request.CurrentReading,
            IsInitialReading = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = request.CreatedByUserId,
            SubmittedByMember = request.SubmittedByMember
        };

        Charge? openingDebtCharge = null;
        if (request.OpeningDebtAmount > 0m)
        {
            var chargeType = await GetOrCreateElectricityChargeTypeAsync(cancellationToken);
            openingDebtCharge = new Charge
            {
                PlotId = meter.BillingPlotId,
                ChargeType = chargeType,
                Amount = RoundMoney(request.OpeningDebtAmount),
                ChargeDate = request.ReadingDate,
                Description = BuildOpeningDebtDescription(request.ReadingDate),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = request.CreatedByUserId
            };
        }

        IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational())
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            _dbContext.MemberElectricityReadings.Add(reading);

            if (openingDebtCharge is not null)
            {
                _dbContext.Charges.Add(openingDebtCharge);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ElectricityReadingOperationResult.Success(reading.Id, openingDebtCharge?.Id, openingDebtCharge?.Amount);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            if (await HasInitialReadingAsync(request.MeterId, cancellationToken))
            {
                return ElectricityReadingOperationResult.Failure("Начальные показания можно внести только один раз.");
            }

            if (await HasReadingOnDateAsync(request.MeterId, request.ReadingDate, cancellationToken))
            {
                return ElectricityReadingOperationResult.Failure("Показания на эту дату для счетчика уже существуют.");
            }

            return ElectricityReadingOperationResult.Failure("Не удалось сохранить начальные показания и задолженность. Повторите попытку.");
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<MemberElectricityMeterOperationResult> CreateMeterAsync(CreateMemberElectricityMeterRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await ValidateMeterRequestAsync(request.MemberId, request.BillingPlotId, request.PlotIds, null, cancellationToken);
        if (!validationResult.Succeeded)
        {
            return MemberElectricityMeterOperationResult.Failure(validationResult.ErrorMessage ?? "Не удалось сохранить счетчик.");
        }

        var meter = new MemberElectricityMeter
        {
            MemberId = request.MemberId,
            MeterNumber = Normalize(request.MeterNumber),
            Name = Normalize(request.Name),
            IsActive = request.IsActive,
            BillingPlotId = request.BillingPlotId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = request.CreatedByUserId,
            MeterPlots = validationResult.ValidPlotIds
                .Select(plotId => new MemberElectricityMeterPlot { PlotId = plotId })
                .ToList()
        };

        _dbContext.MemberElectricityMeters.Add(meter);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MemberElectricityMeterOperationResult.Success(meter.Id);
    }

    public async Task<MemberElectricityMeterInitializationOperationResult> CreateMeterWithInitialReadingAsync(CreateMemberElectricityMeterInitializationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.CurrentReading < 0m)
        {
            return MemberElectricityMeterInitializationOperationResult.Failure("Показание не может быть отрицательным.");
        }

        if (request.OpeningDebtAmount < 0m)
        {
            return MemberElectricityMeterInitializationOperationResult.Failure("Начальная задолженность не может быть отрицательной.");
        }

        var validationResult = await ValidateMeterRequestAsync(request.MemberId, request.BillingPlotId, request.PlotIds, null, cancellationToken);
        if (!validationResult.Succeeded)
        {
            return MemberElectricityMeterInitializationOperationResult.Failure(validationResult.ErrorMessage ?? "Не удалось сохранить счётчик.");
        }

        var meter = new MemberElectricityMeter
        {
            MemberId = request.MemberId,
            MeterNumber = Normalize(request.MeterNumber),
            Name = Normalize(request.Name),
            IsActive = request.IsActive,
            BillingPlotId = request.BillingPlotId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = request.CreatedByUserId,
            MeterPlots = validationResult.ValidPlotIds
                .Select(plotId => new MemberElectricityMeterPlot { PlotId = plotId })
                .ToList()
        };

        var reading = new MemberElectricityReading
        {
            MemberElectricityMeter = meter,
            ReadingDate = request.ReadingDate,
            CurrentReading = request.CurrentReading,
            IsInitialReading = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = request.CreatedByUserId,
            SubmittedByMember = request.SubmittedByMember
        };

        Charge? openingDebtCharge = null;
        if (request.OpeningDebtAmount > 0m)
        {
            var chargeType = await GetOrCreateElectricityChargeTypeAsync(cancellationToken);
            openingDebtCharge = new Charge
            {
                PlotId = request.BillingPlotId,
                ChargeType = chargeType,
                Amount = RoundMoney(request.OpeningDebtAmount),
                ChargeDate = request.ReadingDate,
                Description = BuildOpeningDebtDescription(request.ReadingDate),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = request.CreatedByUserId
            };
        }

        IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational())
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            _dbContext.MemberElectricityMeters.Add(meter);
            _dbContext.MemberElectricityReadings.Add(reading);

            if (openingDebtCharge is not null)
            {
                _dbContext.Charges.Add(openingDebtCharge);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return MemberElectricityMeterInitializationOperationResult.Success(meter.Id, reading.Id, openingDebtCharge?.Id, openingDebtCharge?.Amount);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return MemberElectricityMeterInitializationOperationResult.Failure("Не удалось создать счётчик и сохранить начальные показания. Повторите попытку.");
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<MemberElectricityMeterOperationResult> UpdateMeterAsync(UpdateMemberElectricityMeterRequest request, CancellationToken cancellationToken = default)
    {
        var meter = await _dbContext.MemberElectricityMeters
            .Include(item => item.MeterPlots)
            .Include(item => item.Readings)
            .FirstOrDefaultAsync(item => item.Id == request.MeterId, cancellationToken);

        if (meter is null)
        {
            return MemberElectricityMeterOperationResult.Failure("Счетчик не найден.");
        }

        if (meter.MemberId != request.MemberId)
        {
            return MemberElectricityMeterOperationResult.Failure("В этой версии нельзя менять владельца счетчика.");
        }

        var validationResult = await ValidateMeterRequestAsync(request.MemberId, request.BillingPlotId, request.PlotIds, request.MeterId, cancellationToken);
        if (!validationResult.Succeeded)
        {
            return MemberElectricityMeterOperationResult.Failure(validationResult.ErrorMessage ?? "Не удалось обновить счетчик.");
        }

        meter.MemberId = request.MemberId;
        meter.MeterNumber = Normalize(request.MeterNumber);
        meter.Name = Normalize(request.Name);
        meter.IsActive = request.IsActive;
        meter.BillingPlotId = request.BillingPlotId;

        IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational())
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        _dbContext.MemberElectricityMeterPlots.RemoveRange(meter.MeterPlots);
        meter.MeterPlots = validationResult.ValidPlotIds
            .Select(plotId => new MemberElectricityMeterPlot
            {
                MemberElectricityMeterId = meter.Id,
                PlotId = plotId
            })
            .ToList();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        return MemberElectricityMeterOperationResult.Success(meter.Id);
    }

    public async Task<MemberElectricityReadingEntryContext?> GetReadingEntryContextAsync(int meterId, DateOnly readingDate, decimal? currentReading, CancellationToken cancellationToken = default)
    {
        var meter = await _dbContext.MemberElectricityMeters
            .AsNoTracking()
            .Where(item => item.Id == meterId)
            .Select(item => new
            {
                item.Id,
                item.MemberId,
                MemberName = item.Member != null ? item.Member.FullName : "—",
                DisplayName = !string.IsNullOrWhiteSpace(item.Name)
                    ? item.Name
                    : !string.IsNullOrWhiteSpace(item.MeterNumber)
                        ? item.MeterNumber
                        : $"Счетчик #{item.Id}",
                item.IsActive,
                item.BillingPlotId,
                BillingPlotNumber = item.BillingPlot != null ? item.BillingPlot.Number : "—",
                LinkedPlotIds = item.MeterPlots
                    .Select(link => link.PlotId)
                    .ToList(),
                LinkedPlotNumbers = item.MeterPlots
                    .OrderBy(link => link.Plot != null ? link.Plot.Number : string.Empty)
                    .Select(link => link.Plot != null ? link.Plot.Number : "—")
                    .ToList(),
                HasInitialReading = item.Readings.Any(reading => reading.IsInitialReading),
                PreviousReadingDate = item.Readings
                    .OrderByDescending(reading => reading.ReadingDate)
                    .ThenByDescending(reading => reading.Id)
                    .Select(reading => (DateOnly?)reading.ReadingDate)
                    .FirstOrDefault(),
                PreviousReading = item.Readings
                    .OrderByDescending(reading => reading.ReadingDate)
                    .ThenByDescending(reading => reading.Id)
                    .Select(reading => (decimal?)reading.CurrentReading)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (meter is null)
        {
            return null;
        }

        var currentDate = DateOnly.FromDateTime(DateTime.Today);
        var ownedPlotIds = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(meter.MemberId, currentDate)
            .Where(ownership => meter.LinkedPlotIds.Contains(ownership.PlotId))
            .Select(ownership => ownership.PlotId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var tariff = await GetApplicableMemberTariffAsync(readingDate, cancellationToken);

        var consumption = currentReading.HasValue && meter.PreviousReading.HasValue
            ? currentReading.Value - meter.PreviousReading.Value
            : (decimal?)null;

        var amount = tariff is not null && consumption.HasValue && consumption.Value >= 0m
            ? RoundMoney(consumption.Value * tariff.Rate)
            : (decimal?)null;

        return new MemberElectricityReadingEntryContext(
            meter.Id,
            meter.MemberId,
            meter.MemberName,
            meter.DisplayName,
            meter.IsActive,
            meter.BillingPlotId,
            meter.BillingPlotNumber,
            meter.LinkedPlotIds,
            meter.LinkedPlotNumbers,
            meter.LinkedPlotIds.Contains(meter.BillingPlotId),
            ownedPlotIds.Contains(meter.BillingPlotId),
            meter.HasInitialReading,
            meter.PreviousReadingDate,
            meter.PreviousReading,
            tariff,
            consumption,
            amount);
    }

    public async Task<ElectricityReadingOperationResult> CreateInitialReadingAsync(CreateMemberElectricityInitialReadingRequest request, CancellationToken cancellationToken = default)
    {
        if (request.CurrentReading < 0m)
        {
            return ElectricityReadingOperationResult.Failure("Показание не может быть отрицательным.");
        }

        var meterExists = await _dbContext.MemberElectricityMeters
            .AsNoTracking()
            .AnyAsync(meter => meter.Id == request.MeterId, cancellationToken);

        if (!meterExists)
        {
            return ElectricityReadingOperationResult.Failure("Счетчик не найден.");
        }

        var hasHistory = await _dbContext.MemberElectricityReadings
            .AsNoTracking()
            .AnyAsync(reading => reading.MemberElectricityMeterId == request.MeterId, cancellationToken);

        if (hasHistory)
        {
            return ElectricityReadingOperationResult.Failure("Начальные показания можно внести только один раз.");
        }

        var reading = new MemberElectricityReading
        {
            MemberElectricityMeterId = request.MeterId,
            ReadingDate = request.ReadingDate,
            CurrentReading = request.CurrentReading,
            IsInitialReading = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = request.CreatedByUserId,
            SubmittedByMember = request.SubmittedByMember
        };

        _dbContext.MemberElectricityReadings.Add(reading);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ElectricityReadingOperationResult.Success(reading.Id, null, null);
        }
        catch (DbUpdateException)
        {
            if (await HasInitialReadingAsync(request.MeterId, cancellationToken))
            {
                return ElectricityReadingOperationResult.Failure("Начальные показания можно внести только один раз.");
            }

            if (await HasReadingOnDateAsync(request.MeterId, request.ReadingDate, cancellationToken))
            {
                return ElectricityReadingOperationResult.Failure("Показания на эту дату для счетчика уже существуют.");
            }

            return ElectricityReadingOperationResult.Failure("Не удалось сохранить начальные показания. Повторите попытку.");
        }
    }

    public async Task<ElectricityReadingOperationResult> CreateReadingAsync(CreateMemberElectricityReadingRequest request, CancellationToken cancellationToken = default)
    {
        if (request.CurrentReading < 0m)
        {
            return ElectricityReadingOperationResult.Failure("Показание не может быть отрицательным.");
        }

        var meter = await GetReadingEntryContextAsync(request.MeterId, request.ReadingDate, request.CurrentReading, cancellationToken);

        if (meter is null)
        {
            return ElectricityReadingOperationResult.Failure("Счетчик не найден.");
        }

        if (!meter.IsActive)
        {
            return ElectricityReadingOperationResult.Failure("Счетчик деактивирован. Передача показаний недоступна.");
        }

        if (!meter.BillingPlotIsLinked)
        {
            return ElectricityReadingOperationResult.Failure("Расчетный участок больше не привязан к счетчику.");
        }

        if (!meter.BillingPlotIsOwnedByMember)
        {
            return ElectricityReadingOperationResult.Failure("Расчетный участок больше не принадлежит владельцу счетчика.");
        }

        if (!meter.HasInitialReading || !meter.PreviousReadingDate.HasValue || !meter.PreviousReading.HasValue)
        {
            return ElectricityReadingOperationResult.Failure("Начальные показания ещё не установлены администратором.");
        }

        if (request.ReadingDate <= meter.PreviousReadingDate.Value)
        {
            return ElectricityReadingOperationResult.Failure("Дата новых показаний должна быть позже последней сохраненной даты.");
        }

        if (request.CurrentReading < meter.PreviousReading.Value)
        {
            return ElectricityReadingOperationResult.Failure("Текущее показание не может быть меньше предыдущего.");
        }

        var readingIncrease = request.CurrentReading - meter.PreviousReading.Value;
        if (readingIncrease > MaxReadingIncrease)
        {
            return ElectricityReadingOperationResult.Failure($"Изменение показаний не может превышать {MaxReadingIncrease:0} кВт·ч.");
        }

        if (meter.Tariff is null)
        {
            return ElectricityReadingOperationResult.Failure("Для указанной даты не найден тариф для участников.");
        }

        var tariff = meter.Tariff.Rate;
        var consumption = meter.Consumption ?? 0m;
        var amount = meter.Amount ?? 0m;
        var chargeType = await GetOrCreateElectricityChargeTypeAsync(cancellationToken);

        var charge = new Charge
        {
            PlotId = meter.BillingPlotId,
            ChargeType = chargeType,
            Amount = amount,
            ChargeDate = request.ReadingDate,
            Description = BuildChargeDescription(request.ReadingDate, consumption),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = request.CreatedByUserId
        };

        var reading = new MemberElectricityReading
        {
            MemberElectricityMeterId = request.MeterId,
            ReadingDate = request.ReadingDate,
            PreviousReading = meter.PreviousReading.Value,
            CurrentReading = request.CurrentReading,
            Consumption = consumption,
            AppliedMemberRate = tariff,
            Amount = amount,
            IsInitialReading = false,
            Charge = charge,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = request.CreatedByUserId,
            SubmittedByMember = request.SubmittedByMember
        };

        IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational())
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            _dbContext.MemberElectricityReadings.Add(reading);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ElectricityReadingOperationResult.Success(reading.Id, reading.ChargeId, amount);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            if (await HasReadingOnDateAsync(request.MeterId, request.ReadingDate, cancellationToken))
            {
                return ElectricityReadingOperationResult.Failure("Показания на эту дату для счетчика уже существуют.");
            }

            return ElectricityReadingOperationResult.Failure("Не удалось сохранить показания и начисление. Повторите попытку.");
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<MemberElectricityMeterValidationResult> ValidateMeterRequestAsync(int memberId, int billingPlotId, IReadOnlyCollection<int> plotIds, int? existingMeterId, CancellationToken cancellationToken)
    {
        if (plotIds.Count == 0)
        {
            return MemberElectricityMeterValidationResult.Failure("Выберите хотя бы один участок для счетчика.");
        }

        var distinctPlotIds = plotIds
            .Where(plotId => plotId > 0)
            .Distinct()
            .ToArray();

        if (distinctPlotIds.Length == 0)
        {
            return MemberElectricityMeterValidationResult.Failure("Выберите хотя бы один участок для счетчика.");
        }

        if (!distinctPlotIds.Contains(billingPlotId))
        {
            return MemberElectricityMeterValidationResult.Failure("Расчетный участок должен входить в список привязанных участков.");
        }

        var memberExists = await _dbContext.Members
            .AsNoTracking()
            .AnyAsync(member => member.Id == memberId, cancellationToken);

        if (!memberExists)
        {
            return MemberElectricityMeterValidationResult.Failure("Участник не найден.");
        }

        var currentDate = DateOnly.FromDateTime(DateTime.Today);
        var ownedPlotIds = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(memberId, currentDate)
            .Where(ownership => distinctPlotIds.Contains(ownership.PlotId))
            .Select(ownership => ownership.PlotId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (ownedPlotIds.Count != distinctPlotIds.Length)
        {
            return MemberElectricityMeterValidationResult.Failure("Можно привязать только участки, которые сейчас принадлежат выбранному участнику.");
        }

        var conflictingPlotId = await _dbContext.MemberElectricityMeterPlots
            .AsNoTracking()
            .Where(link => distinctPlotIds.Contains(link.PlotId)
                && link.MemberElectricityMeter != null
                && link.MemberElectricityMeter.IsActive
                && (!existingMeterId.HasValue || link.MemberElectricityMeterId != existingMeterId.Value))
            .Select(link => (int?)link.PlotId)
            .FirstOrDefaultAsync(cancellationToken);

        if (conflictingPlotId.HasValue)
        {
            return MemberElectricityMeterValidationResult.Failure("Один из выбранных участков уже привязан к другому активному счетчику.");
        }

        return MemberElectricityMeterValidationResult.Success(distinctPlotIds);
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

    private async Task<MemberElectricityTariffSnapshot?> GetApplicableMemberTariffAsync(DateOnly readingDate, CancellationToken cancellationToken)
    {
        var memberTariff = await _dbContext.MemberElectricityTariffs
            .AsNoTracking()
            .Where(item => item.EffectiveFrom <= readingDate)
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenByDescending(item => item.Id)
            .Select(item => new MemberElectricityTariffSnapshot(item.EffectiveFrom, item.Rate))
            .FirstOrDefaultAsync(cancellationToken);

        if (memberTariff is not null)
        {
            return memberTariff;
        }

        return await _dbContext.ElectricityTariffs
            .AsNoTracking()
            .Where(item => item.EffectiveFrom <= readingDate)
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenByDescending(item => item.Id)
            .Select(item => new MemberElectricityTariffSnapshot(item.EffectiveFrom, item.DayRate))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string BuildChargeDescription(DateOnly readingDate, decimal consumption)
    {
        return string.Format(
            RussianCulture,
            "Электроэнергия за {0:dd.MM.yyyy}: расход {1:0.000} кВт·ч",
            readingDate.ToDateTime(TimeOnly.MinValue),
            consumption);
    }

    private static string BuildOpeningDebtDescription(DateOnly readingDate)
    {
        return string.Format(
            RussianCulture,
            "Начальная задолженность по электроэнергии на {0:dd.MM.yyyy}",
            readingDate.ToDateTime(TimeOnly.MinValue));
    }

    private static decimal RoundMoney(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private Task<bool> HasInitialReadingAsync(int meterId, CancellationToken cancellationToken)
    {
        return _dbContext.MemberElectricityReadings
            .AsNoTracking()
            .AnyAsync(reading => reading.MemberElectricityMeterId == meterId && reading.IsInitialReading, cancellationToken);
    }

    private Task<bool> HasReadingOnDateAsync(int meterId, DateOnly readingDate, CancellationToken cancellationToken)
    {
        return _dbContext.MemberElectricityReadings
            .AsNoTracking()
            .AnyAsync(reading => reading.MemberElectricityMeterId == meterId && reading.ReadingDate == readingDate, cancellationToken);
    }

    private static bool IsMeterReadingDuplicateViolation(DbUpdateException exception)
    {
        return exception.Message.Contains("MemberElectricityReadings", StringComparison.OrdinalIgnoreCase)
            || exception.InnerException?.Message.Contains("MemberElectricityReadings", StringComparison.OrdinalIgnoreCase) == true;
    }

    private sealed record MemberElectricityMeterValidationResult(bool Succeeded, string? ErrorMessage, int[] ValidPlotIds)
    {
        public static MemberElectricityMeterValidationResult Success(int[] validPlotIds) => new(true, null, validPlotIds);

        public static MemberElectricityMeterValidationResult Failure(string errorMessage) => new(false, errorMessage, []);
    }
}
