namespace Neftyanik.Portal.Domain.Entities;

public sealed class PlatformAuditLog
{
    public long Id { get; set; }
    public int AssociationId { get; set; }
    public string OperatorUserId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string OldValuesJson { get; set; } = string.Empty;
    public string NewValuesJson { get; set; } = string.Empty;
}
