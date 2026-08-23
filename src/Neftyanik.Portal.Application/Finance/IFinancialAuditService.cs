namespace Neftyanik.Portal.Application.Finance;

public interface IFinancialAuditService
{
    void Add(
        string action,
        string entityType,
        string entityId,
        string? description = null,
        object? oldValues = null,
        object? newValues = null);
}
