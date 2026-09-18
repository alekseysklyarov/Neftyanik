namespace Neftyanik.Portal.Domain.Entities;

public sealed class AssociationUserMembership : IAssociationOwned
{
    public int Id { get; set; }
    public int AssociationId { get; set; }
    public Association Association { get; set; } = null!;
    public string ApplicationUserId { get; set; } = string.Empty;
    public ApplicationUser ApplicationUser { get; set; } = null!;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
