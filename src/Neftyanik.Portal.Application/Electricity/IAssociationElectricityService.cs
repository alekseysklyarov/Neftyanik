namespace Neftyanik.Portal.Application.Electricity;

public interface IAssociationElectricityService
{
    Task<ElectricityOperationResult> CreateTariffAsync(CreateAssociationElectricityTariffRequest request, CancellationToken cancellationToken = default);

    Task<ElectricityReadingOperationResult> CreateInitialReadingAsync(CreateAssociationElectricityInitialReadingRequest request, CancellationToken cancellationToken = default);

    Task<ElectricityReadingOperationResult> CreateReadingAsync(CreateAssociationElectricityReadingRequest request, CancellationToken cancellationToken = default);
}

public sealed record CreateAssociationElectricityTariffRequest(
    DateOnly EffectiveFrom,
    decimal DayRate,
    decimal NightRate,
    string? CreatedByUserId);

public sealed record CreateAssociationElectricityInitialReadingRequest(
    DateOnly ReadingDate,
    decimal CurrentDayReading,
    decimal CurrentNightReading,
    string? CreatedByUserId);

public sealed record CreateAssociationElectricityReadingRequest(
    DateOnly ReadingDate,
    decimal CurrentDayReading,
    decimal CurrentNightReading,
    string? CreatedByUserId);
