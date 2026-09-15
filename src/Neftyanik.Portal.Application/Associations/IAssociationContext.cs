using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Application.Associations;

public interface IAssociationContext
{
    bool IsResolved { get; }
    int AssociationId { get; }
    string Slug { get; }
    Association Association { get; }
}
