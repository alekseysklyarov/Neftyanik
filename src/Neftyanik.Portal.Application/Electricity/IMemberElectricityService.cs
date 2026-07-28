namespace Neftyanik.Portal.Application.Electricity;

public interface IMemberElectricityService
{
    Task<ElectricityOperationResult> CreateTariffAsync(CreateMemberElectricityTariffRequest request, CancellationToken cancellationToken = default);

    Task<MemberElectricityMeterOperationResult> CreateMeterAsync(CreateMemberElectricityMeterRequest request, CancellationToken cancellationToken = default);

    Task<MemberElectricityMeterOperationResult> UpdateMeterAsync(UpdateMemberElectricityMeterRequest request, CancellationToken cancellationToken = default);

    Task<MemberElectricityReadingEntryContext?> GetReadingEntryContextAsync(int meterId, DateOnly readingDate, decimal? currentReading, CancellationToken cancellationToken = default);

    Task<ElectricityReadingOperationResult> CreateInitialReadingAsync(CreateMemberElectricityInitialReadingRequest request, CancellationToken cancellationToken = default);

    Task<ElectricityReadingOperationResult> CreateReadingAsync(CreateMemberElectricityReadingRequest request, CancellationToken cancellationToken = default);
}

public sealed record CreateMemberElectricityTariffRequest(
    DateOnly EffectiveFrom,
    decimal Rate,
    string? CreatedByUserId);

public sealed record CreateMemberElectricityMeterRequest(
    int MemberId,
    string? MeterNumber,
    string? Name,
    bool IsActive,
    int BillingPlotId,
    IReadOnlyCollection<int> PlotIds,
    string? CreatedByUserId);

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
    string? CreatedByUserId,
    bool SubmittedByMember = false);

public sealed record CreateMemberElectricityReadingRequest(
    int MeterId,
    DateOnly ReadingDate,
    decimal CurrentReading,
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

public sealed record MemberElectricityReadingEntryContext(
    int MeterId,
    int MemberId,
    string MemberName,
    string DisplayName,
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
    MemberElectricityTariffSnapshot? Tariff,
    decimal? Consumption,
    decimal? Amount);

public sealed record MemberElectricityTariffSnapshot(
    DateOnly EffectiveFrom,
    decimal Rate);
