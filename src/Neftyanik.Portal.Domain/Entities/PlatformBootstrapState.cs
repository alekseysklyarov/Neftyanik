using Neftyanik.Portal.Domain.Enums;

namespace Neftyanik.Portal.Domain.Entities;

public sealed class PlatformBootstrapState
{
    public int Id { get; set; }
    public PlatformBootstrapDisposition Disposition { get; set; }
    public DateTimeOffset ConsumedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? OperatorIdentity { get; set; }
    public string? ApprovalReference { get; set; }
    public DateTimeOffset? InitializedAtUtc { get; set; }
    public string? InitializedUserId { get; set; }
}
