namespace Neftyanik.Portal.Domain.Entities;

public sealed class PlatformPasswordRecoveryAudit
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string OperatorIdentity { get; set; } = string.Empty;
    public string ApprovalReference { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
}
