namespace Neftyanik.Portal.Domain.Entities;

public sealed class AssociationAccountBinding
{
    public string ApplicationUserId { get; set; } = string.Empty;
    public int AssociationId { get; set; }
}
