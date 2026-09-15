using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Application.Associations;

public sealed class AssociationContext : IAssociationContext
{
    private Association? _association;

    public bool IsResolved => _association is not null;
    public int AssociationId { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public Association Association => _association ?? throw new AssociationIsolationException("An association has not been resolved.");

    public void Resolve(Association association)
    {
        ArgumentNullException.ThrowIfNull(association);
        association.ValidateSlug();
        if (!association.IsActive || association.Id <= 0)
        {
            throw new AssociationIsolationException("Only an existing active association can be resolved.");
        }
        if (IsResolved)
        {
            throw new AssociationIsolationException("The association cannot be changed within a scope.");
        }
        AssociationId = association.Id;
        Slug = association.Slug;
        _association = association;
    }
}
