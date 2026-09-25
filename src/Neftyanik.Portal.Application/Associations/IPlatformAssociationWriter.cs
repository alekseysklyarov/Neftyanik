namespace Neftyanik.Portal.Application.Associations;

public interface IPlatformAssociationWriteAccess
{
    Task<string> GetOperatorIdAsync(CancellationToken cancellationToken = default);
}

public interface IPlatformAssociationWriter
{
    Task<PlatformAssociationEditState?> GetAsync(int associationId, CancellationToken cancellationToken = default);
    Task<AssociationWriteResult> UpdateAsync(int associationId, AssociationMetadataUpdate update, CancellationToken cancellationToken = default);
    Task<AssociationWriteResult> SetActiveAsync(int associationId, bool isActive, Guid revision, CancellationToken cancellationToken = default);
}

public sealed record AssociationMetadataUpdate(
    string Name, string? ContactEmail, string? ContactPhone, string? PostalAddress, Guid Revision);

public sealed record PlatformAssociationEditState(
    int Id, string Slug, string Name, string? ContactEmail, string? ContactPhone, string? PostalAddress,
    bool IsActive, Guid Revision, bool CanDeactivate);

public enum AssociationWriteOutcome
{
    Saved, Unchanged, NotFound, InvalidInput, Conflict, ProtectedAssociation, Failed
}

public sealed record AssociationWriteResult(AssociationWriteOutcome Outcome, string? InvalidField = null);
