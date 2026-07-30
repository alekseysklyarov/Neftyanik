namespace Neftyanik.Portal.Application.Electricity;

using Neftyanik.Portal.Domain.Enums;

public interface IMemberElectricityService
{
    Task<ElectricityOperationResult> CreateTariffAsync(CreateMemberElectricityTariffRequest request, CancellationToken cancellationToken = default);

    Task<MemberElectricityMeterOperationResult> CreateMeterAsync(CreateMemberElectricityMeterRequest request, CancellationToken cancellationToken = default);

    Task<MemberElectricityMeterInitializationOperationResult> CreateMeterWithInitialReadingAsync(CreateMemberElectricityMeterInitializationRequest request, CancellationToken cancellationToken = default);

    Task<MemberElectricityMeterOperationResult> UpdateMeterAsync(UpdateMemberElectricityMeterRequest request, CancellationToken cancellationToken = default);

    Task<MemberElectricityReadingEntryContext?> GetReadingEntryContextAsync(int meterId, DateOnly readingDate, decimal? currentReading, decimal? currentNightReading, CancellationToken cancellationToken = default);

    Task<ElectricityReadingOperationResult> CreateInitialReadingAsync(CreateMemberElectricityInitialReadingRequest request, CancellationToken cancellationToken = default);

    Task<ElectricityReadingOperationResult> CreateInitialReadingWithDebtAsync(CreateMemberElectricityInitializationRequest request, CancellationToken cancellationToken = default);

    Task<ElectricityReadingOperationResult> CreateReadingAsync(CreateMemberElectricityReadingRequest request, CancellationToken cancellationToken = default);
}

public sealed record CreateMemberElectricityTariffRequest(
    DateOnly EffectiveFrom,
    decimal Rate,
    decimal? NightRate,
    string? CreatedByUserId);

public sealed record CreateMemberElectricityMeterRequest(
    int MemberId,
    string? MeterNumber,
    string? Name,
    bool IsActive,
    int BillingPlotId,
    IReadOnlyCollection<int> PlotIds,
    string? CreatedByUserId);

public sealed record CreateMemberElectricityMeterInitializationRequest(
    int MemberId,
    string? MeterNumber,
    string? Name,
    bool IsActive,
    int BillingPlotId,
    IReadOnlyCollection<int> PlotIds,
    DateOnly ReadingDate,
    decimal CurrentReading,
    decimal? CurrentNightReading,
    decimal OpeningDebtAmount,
    string? CreatedByUserId,
    bool SubmittedByMember = false);

public sealed record UpdateMemberElectricityMeterRequest(
    int MeterId,
    int MemberId,
    string? MeterNumber,
    string? Name,
    bool IsActive,
    int BillingPlotId,
    IReadOnlyCollection<int> PlotIds,
    string? UpdatedByUserId);

public sealed record CreateMemberElectricityInitialReadingRequest(
    int MeterId,
    DateOnly ReadingDate,
    decimal CurrentReading,
    decimal? CurrentNightReading,
    string? CreatedByUserId,
    bool SubmittedByMember = false);

public sealed record CreateMemberElectricityInitializationRequest(
    int MeterId,
    DateOnly ReadingDate,
    decimal CurrentReading,
    decimal? CurrentNightReading,
    decimal OpeningDebtAmount,
    string? CreatedByUserId,
    bool SubmittedByMember = false);

public sealed record CreateMemberElectricityReadingRequest(
    int MeterId,
    DateOnly ReadingDate,
    decimal CurrentReading,
    decimal? CurrentNightReading,
    string? CreatedByUserId,
    bool SubmittedByMember = false);

public sealed record MemberElectricityMeterOperationResult(
    bool Succeeded,
    string? ErrorMessage,
    int? MeterId) : ElectricityOperationResult(Succeeded, ErrorMessage)
{
    public static MemberElectricityMeterOperationResult Success(int meterId) => new(true, null, meterId);

    public static new MemberElectricityMeterOperationResult Failure(string errorMessage) => new(false, errorMessage, null);
}

public sealed record MemberElectricityMeterInitializationOperationResult(
    bool Succeeded,
    string? ErrorMessage,
    int? MeterId,
    long? ReadingId,
    long? ChargeId,
    decimal? TotalAmount) : ElectricityOperationResult(Succeeded, ErrorMessage)
{
    public static MemberElectricityMeterInitializationOperationResult Success(int meterId, long readingId, long? chargeId, decimal? totalAmount)
        => new(true, null, meterId, readingId, chargeId, totalAmount);

    public static new MemberElectricityMeterInitializationOperationResult Failure(string errorMessage)
        => new(false, errorMessage, null, null, null, null);
}

public sealed record MemberElectricityReadingEntryContext(
    int MeterId,
    int MemberId,
    string MemberName,
    string DisplayName,
    MemberElectricityMeterType MeterType,
    bool IsActive,
    int BillingPlotId,
    string BillingPlotNumber,
    IReadOnlyList<int> LinkedPlotIds,
    IReadOnlyList<string> LinkedPlotNumbers,
    bool BillingPlotIsLinked,
    bool BillingPlotIsOwnedByMember,
    bool HasInitialReading,
    DateOnly? PreviousReadingDate,
    decimal? PreviousReading,
    decimal? PreviousNightReading,
    MemberElectricityTariffSnapshot? Tariff,
    decimal? Consumption,
    decimal? Amount);

public sealed record MemberElectricityTariffSnapshot(
    DateOnly EffectiveFrom,
    decimal Rate,
    decimal? NightRate);
