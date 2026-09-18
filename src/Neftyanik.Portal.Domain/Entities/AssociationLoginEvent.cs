namespace Neftyanik.Portal.Domain.Entities;

public sealed class AssociationLoginEvent : IAssociationOwned
{
    public int Id { get; set; }
    public int AssociationId { get; set; }
    public Association Association { get; set; } = null!;
    public long UserLoginHistoryId { get; set; }
    public UserLoginHistory UserLoginHistory { get; set; } = null!;
}
