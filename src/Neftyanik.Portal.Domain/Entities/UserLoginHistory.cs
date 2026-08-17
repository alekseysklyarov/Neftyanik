namespace Neftyanik.Portal.Domain.Entities;

public class UserLoginHistory
{
    public const int IpAddressMaxLength = 45;
    public const int UserAgentMaxLength = 512;

    public long Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    public DateTimeOffset LoggedInAtUtc { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }
}
