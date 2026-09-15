namespace Neftyanik.Portal.Domain.Entities;

public interface IAssociationOwned
{
    int AssociationId { get; set; }

    Association? Association { get; set; }
}
