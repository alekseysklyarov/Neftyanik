namespace Neftyanik.Portal.Application.Electricity;

public interface IAssociationElectricityService
{
    Task<ElectricityOperationResult> CreateTariffAsync(CreateAssociationElectricityTariffRequest request, CancellationToken cancellationToken = default);

    Task<ElectricityReadingOperationResult> CreateInitialReadingAsync(CreateAssociationElectricityInitialReadingRequest request, CancellationToken cancellationToken = default);

    Task<ElectricityReadingOperationResult> CreateReadingAsync(CreateAssociationElectricityReadingRequest request, CancellationToken cancellationToken = default);

    Task<AssociationElectricityExpenseOperationResult> CreateExpenseAsync(CreateAssociationElectricityExpenseRequest request, CancellationToken cancellationToken = default);
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

public sealed record CreateAssociationElectricityExpenseRequest(
    long ReadingId,
    string? CreatedByUserId);

public sealed record AssociationElectricityExpenseOperationResult(
    bool Succeeded,
    string? ErrorMessage,
    long? ExpenseId,
    decimal? TotalAmount) : ElectricityOperationResult(Succeeded, ErrorMessage)
{
    public static AssociationElectricityExpenseOperationResult Success(long expenseId, decimal totalAmount)
        => new(true, null, expenseId, totalAmount);

    public static new AssociationElectricityExpenseOperationResult Failure(string errorMessage)
        => new(false, errorMessage, null, null);
}
