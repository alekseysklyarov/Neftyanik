namespace Neftyanik.Portal.Domain.Entities;

public class FinancialAuditLog
{
    public const int ActionMaxLength = 50;
    public const int EntityTypeMaxLength = 100;
    public const int EntityIdMaxLength = 100;
    public const int UserIdMaxLength = 450;
    public const int UserNameMaxLength = 256;
    public const int DescriptionMaxLength = 1000;

    public long Id { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string? UserId { get; set; }

    public string? UserName { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? OldValuesJson { get; set; }

    public string? NewValuesJson { get; set; }
}
