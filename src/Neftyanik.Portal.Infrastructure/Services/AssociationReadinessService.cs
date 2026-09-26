using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class AssociationReadinessService(ApplicationDbContext database, TimeProvider clock) : IAssociationReadinessService
{
    private const string CashSettingKey = "Finance.CashInitialization";

    public async Task<AssociationReadinessSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!database.IsAssociationResolved)
            throw new AssociationIsolationException("Resolve an association before reading readiness.");

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var memberTariff = await database.MemberElectricityTariffs.AsNoTracking()
            .Where(x => x.EffectiveFrom <= today).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
            .Select(x => new { x.EffectiveFrom, x.NightRate }).FirstOrDefaultAsync(cancellationToken);
        var supplierTariffDate = await database.AssociationElectricityTariffs.AsNoTracking()
            .Where(x => x.EffectiveFrom <= today).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
            .Select(x => (DateOnly?)x.EffectiveFrom).FirstOrDefaultAsync(cancellationToken);

        var meters = database.MemberElectricityMeters.AsNoTracking().Where(x => x.IsActive);
        var activeMeters = await meters.CountAsync(cancellationToken);
        var nightMeters = await meters.CountAsync(x => x.Member != null && x.Member.ElectricityMeterType == MemberElectricityMeterType.DayNight, cancellationToken);
        var missingInitial = await meters.CountAsync(x => !x.Readings.Any(r => r.IsInitialReading), cancellationToken);
        var missingTariff = memberTariff is null ? activeMeters : memberTariff.NightRate is null ? nightMeters : 0;
        var supplierHistory = await database.AssociationElectricityReadings.AsNoTracking().AnyAsync(cancellationToken);
        var supplierInitial = await database.AssociationElectricityReadings.AsNoTracking().AnyAsync(x => x.IsInitialReading, cancellationToken);
        var activeTypes = await database.ChargeTypes.AsNoTracking().Where(x => x.IsActive)
            .Select(x => x.IsYearly).ToListAsync(cancellationToken);

        var categorySetting = await database.SystemSettings.AsNoTracking()
            .Where(x => x.Key == ElectricityExpenseCategoryQueries.SettingKey)
            .Select(x => new { x.Value }).SingleOrDefaultAsync(cancellationToken);
        var categoryId = await database.GetElectricityExpenseCategoryIdAsync(cancellationToken);
        var categoryActive = categoryId.HasValue && await database.ExpenseCategories.AsNoTracking()
            .AnyAsync(x => x.Id == categoryId.Value && x.IsActive, cancellationToken);
        var manualCategories = await database.ExpenseCategories.AsNoTracking()
            .CountAsync(x => x.IsActive && x.Id != categoryId, cancellationToken);
        var cashSetting = await database.SystemSettings.AsNoTracking().Where(x => x.Key == CashSettingKey)
            .Select(x => new { x.Value }).SingleOrDefaultAsync(cancellationToken);
        var cashDate = ReadCashInitializationDate(cashSetting?.Value);
        var members = await database.Members.AsNoTracking().CountAsync(cancellationToken);
        var plots = await database.Plots.AsNoTracking().CountAsync(cancellationToken);
        var ownerships = await database.PlotOwnerships.AsNoTracking().WhereCurrentOn(today).CountAsync(cancellationToken);

        var facts = new AssociationReadinessFacts
        {
            ActiveMeters = activeMeters, MetersWithoutInitialReading = missingInitial,
            MetersWithoutApplicableTariff = missingTariff, NeedsNightTariff = nightMeters > 0,
            MemberTariffEffectiveFrom = memberTariff?.EffectiveFrom, SupplierTariffEffectiveFrom = supplierTariffDate,
            HasSupplierHistory = supplierHistory, HasSupplierInitialReading = supplierInitial,
            ActiveChargeTypes = activeTypes.Count, YearlyChargeTypes = activeTypes.Count(x => x),
            ActiveManualExpenseCategories = manualCategories, CashInitializedOn = cashDate,
            Members = members, Plots = plots, CurrentOwnerships = ownerships
        };
        var memberCheck = memberTariff is null
            ? new ReadinessCheck(ReadinessArea.MemberElectricity, activeMeters == 0 ? ReadinessStatus.NotUsed : ReadinessStatus.RequiredForOperation, ReadinessReason.NoApplicableTariff)
            : nightMeters > 0 && memberTariff.NightRate is null
                ? new(ReadinessArea.MemberElectricity, ReadinessStatus.RequiredForOperation, ReadinessReason.MissingNightTariff)
                : new(ReadinessArea.MemberElectricity, ReadinessStatus.Ready, ReadinessReason.Configured);
        var supplierReason = !supplierHistory ? ReadinessReason.NoSupplierHistory
            : !supplierInitial && supplierTariffDate is null ? ReadinessReason.MissingTariffAndInitialReading
            : !supplierInitial ? ReadinessReason.MissingInitialReading
            : supplierTariffDate is null ? ReadinessReason.NoApplicableTariff : ReadinessReason.Configured;
        var categoryCheck = categorySetting is null
            ? new ReadinessCheck(ReadinessArea.ElectricityExpenseCategory, categoryActive ? ReadinessStatus.Warning : ReadinessStatus.RequiredForOperation,
                categoryActive ? ReadinessReason.LegacyCategoryFallback : ReadinessReason.MissingCategorySetting)
            : !int.TryParse(categorySetting.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var configuredId) || configuredId <= 0
                ? new(ReadinessArea.ElectricityExpenseCategory, ReadinessStatus.RequiredForOperation, ReadinessReason.InvalidCategorySetting)
                : !categoryActive
                    ? new(ReadinessArea.ElectricityExpenseCategory, ReadinessStatus.RequiredForOperation, ReadinessReason.CategoryUnavailable)
                    : new(ReadinessArea.ElectricityExpenseCategory, ReadinessStatus.Ready, ReadinessReason.Configured);

        ReadinessCheck[] checks =
        [
            memberCheck,
            new(ReadinessArea.SupplierElectricity, !supplierHistory ? ReadinessStatus.NotUsed
                : supplierReason == ReadinessReason.Configured ? ReadinessStatus.Ready : ReadinessStatus.RequiredForOperation, supplierReason),
            new(ReadinessArea.IndividualMeters, activeMeters == 0 ? ReadinessStatus.NotUsed
                : missingInitial > 0 || missingTariff > 0 ? ReadinessStatus.RequiredForOperation : ReadinessStatus.Ready,
                activeMeters == 0 ? ReadinessReason.NoMeters : missingInitial > 0 || missingTariff > 0 ? ReadinessReason.IncompleteMeters : ReadinessReason.Configured),
            new(ReadinessArea.ChargeTypes, activeTypes.Count == 0 ? ReadinessStatus.RequiredForOperation : ReadinessStatus.Ready,
                activeTypes.Count == 0 ? ReadinessReason.NoActiveChargeTypes : ReadinessReason.Configured),
            categoryCheck,
            new(ReadinessArea.ManualExpenseCategories, manualCategories == 0 ? ReadinessStatus.RequiredForOperation
                : categoryId is null ? ReadinessStatus.Warning : ReadinessStatus.Ready,
                manualCategories == 0 ? ReadinessReason.NoManualCategories : categoryId is null ? ReadinessReason.UnknownCategoryMapping : ReadinessReason.Configured),
            new(ReadinessArea.Cash, cashDate.HasValue ? ReadinessStatus.Ready : ReadinessStatus.Warning,
                cashSetting is null ? ReadinessReason.CashStartsAtZero : cashDate.HasValue ? ReadinessReason.Configured : ReadinessReason.InvalidCashInitialization),
            new(ReadinessArea.MembersAndPlots, members == 0 || plots == 0 ? ReadinessStatus.NotUsed
                : ownerships == 0 ? ReadinessStatus.RequiredForOperation : ReadinessStatus.Ready,
                members == 0 || plots == 0 ? ReadinessReason.NoMembersOrPlots : ownerships == 0 ? ReadinessReason.NoCurrentOwnership : ReadinessReason.Configured),
            new(ReadinessArea.MembershipFeeRates, ReadinessStatus.NotImplemented, ReadinessReason.MembershipRatesNotIntegrated)
        ];
        return new(today, checks, facts);
    }

    private static DateOnly? ReadCashInitializationDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            // Read the existing setting format without exposing balances in the readiness DTO.
            var data = JsonSerializer.Deserialize<CashInitializationValue>(value);
            return data is { Amount: >= 0m, AcceptedAt: not null }
                && data.AcceptedAt != DateOnly.MinValue && !string.IsNullOrWhiteSpace(data.AcceptedFrom)
                && (data.AdvancePaymentsAmount is null or >= 0m) ? data.AcceptedAt : null;
        }
        catch (JsonException) { return null; }
    }

    private sealed record CashInitializationValue(decimal? Amount, DateOnly? AcceptedAt, string? AcceptedFrom, decimal? AdvancePaymentsAmount);
}
