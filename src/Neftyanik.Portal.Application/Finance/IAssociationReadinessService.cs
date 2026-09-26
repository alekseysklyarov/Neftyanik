namespace Neftyanik.Portal.Application.Finance;

public interface IAssociationReadinessService
{
    Task<AssociationReadinessSnapshot> GetAsync(CancellationToken cancellationToken = default);
}

public enum ReadinessStatus
{
    Ready,
    RequiredForOperation,
    Warning,
    NotUsed,
    NotImplemented
}

public enum ReadinessArea
{
    MemberElectricity,
    SupplierElectricity,
    IndividualMeters,
    ChargeTypes,
    ElectricityExpenseCategory,
    ManualExpenseCategories,
    Cash,
    MembersAndPlots,
    MembershipFeeRates
}

public enum ReadinessReason
{
    Configured,
    NoApplicableTariff,
    MissingNightTariff,
    NoMeters,
    NoSupplierHistory,
    MissingInitialReading,
    MissingTariffAndInitialReading,
    IncompleteMeters,
    NoActiveChargeTypes,
    MissingCategorySetting,
    LegacyCategoryFallback,
    InvalidCategorySetting,
    CategoryUnavailable,
    NoManualCategories,
    UnknownCategoryMapping,
    CashStartsAtZero,
    InvalidCashInitialization,
    NoMembersOrPlots,
    NoCurrentOwnership,
    MembershipRatesNotIntegrated
}

public sealed record ReadinessCheck(ReadinessArea Area, ReadinessStatus Status, ReadinessReason Reason);

public sealed record AssociationReadinessSnapshot(
    DateOnly AsOfDate, IReadOnlyList<ReadinessCheck> Checks, AssociationReadinessFacts Facts);

public sealed record AssociationReadinessFacts
{
    public int ActiveMeters { get; init; }
    public int MetersWithoutInitialReading { get; init; }
    public int MetersWithoutApplicableTariff { get; init; }
    public bool NeedsNightTariff { get; init; }
    public DateOnly? MemberTariffEffectiveFrom { get; init; }
    public DateOnly? SupplierTariffEffectiveFrom { get; init; }
    public bool HasSupplierHistory { get; init; }
    public bool HasSupplierInitialReading { get; init; }
    public int ActiveChargeTypes { get; init; }
    public int YearlyChargeTypes { get; init; }
    public int ActiveManualExpenseCategories { get; init; }
    public DateOnly? CashInitializedOn { get; init; }
    public int Members { get; init; }
    public int Plots { get; init; }
    public int CurrentOwnerships { get; init; }
}
