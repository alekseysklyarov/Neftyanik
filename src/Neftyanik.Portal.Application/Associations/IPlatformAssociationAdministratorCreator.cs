namespace Neftyanik.Portal.Application.Associations;

public interface IPlatformAssociationAdministratorCreator
{
    Task<AdministratorCreationOutcome> CreateAsync(int associationId, AssociationAdministratorRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AssociationAdministratorRequest(string UserName, string Email, string? DisplayName,
    string TemporaryPassword, bool ConfirmAssignment);

public enum AdministratorCreationOutcome { Created, NotFound, InactiveAssociation, InvalidInput, ConfirmationRequired, UsernameExists, IdentityRejected, Failed }
