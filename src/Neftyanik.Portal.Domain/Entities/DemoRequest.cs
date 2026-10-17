namespace Neftyanik.Portal.Domain.Entities;

// A platform sales inquiry; it never grants access to an association.
public sealed class DemoRequest
{
    public long Id { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public string AssociationName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int? PlotCount { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ContactedAtUtc { get; set; }
    public DateTime? NotificationSentAtUtc { get; set; }
    public DateTime? NotificationNextAttemptAtUtc { get; set; }
    public int NotificationAttemptCount { get; set; }
    public string ConsentVersion { get; set; } = "demo-contact-v1";
}
