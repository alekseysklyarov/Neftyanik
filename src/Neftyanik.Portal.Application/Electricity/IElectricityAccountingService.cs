namespace Neftyanik.Portal.Application.Electricity;

public interface IElectricityAccountingService
{
    Task<ElectricityOperationResult> CreateTariffAsync(CreateElectricityTariffRequest request, CancellationToken cancellationToken = default);

    Task<ElectricityReadingOperationResult> CreateInitialReadingAsync(CreateInitialElectricityReadingRequest request, CancellationToken cancellationToken = default);

    Task<ElectricityReadingOperationResult> CreateReadingAsync(CreateElectricityReadingRequest request, CancellationToken cancellationToken = default);
}

public sealed record CreateElectricityTariffRequest(
    DateOnly EffectiveFrom,
    decimal DayRate,
    decimal NightRate,
    string? CreatedByUserId);

public sealed record CreateInitialElectricityReadingRequest(
    int PlotId,
    DateOnly ReadingDate,
    decimal CurrentDayReading,
    decimal CurrentNightReading,
    string? CreatedByUserId);

public sealed record CreateElectricityReadingRequest(
    int PlotId,
    DateOnly ReadingDate,
    decimal CurrentDayReading,
    decimal CurrentNightReading,
    string? CreatedByUserId);

public record ElectricityOperationResult(bool Succeeded, string? ErrorMessage)
{
    public static ElectricityOperationResult Success() => new(true, null);

    public static ElectricityOperationResult Failure(string errorMessage) => new(false, errorMessage);
}

public sealed record ElectricityReadingOperationResult(
    bool Succeeded,
    string? ErrorMessage,
    long? ReadingId,
    long? ChargeId,
    decimal? TotalAmount) : ElectricityOperationResult(Succeeded, ErrorMessage)
{
    public static ElectricityReadingOperationResult Success(long readingId, long? chargeId, decimal? totalAmount)
        => new(true, null, readingId, chargeId, totalAmount);

    public static new ElectricityReadingOperationResult Failure(string errorMessage)
        => new(false, errorMessage, null, null, null);
}
