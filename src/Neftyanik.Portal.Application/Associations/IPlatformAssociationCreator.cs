namespace Neftyanik.Portal.Application.Associations;

public interface IPlatformAssociationCreator
{
    Task<AssociationCreationResult> CreateAsync(AssociationCreationRequest request, CancellationToken cancellationToken = default);
}

public interface IAssociationSlugReservations
{
    bool IsReserved(string slug);
}

public sealed record AssociationCreationRequest(
    string Name, string Slug, string? ContactEmail, string? ContactPhone, string? PostalAddress,
    string AdministratorEmail, string AdministratorUserName, bool ConfirmAdministrator,
    string TemporaryPassword = "", string? AdministratorDisplayName = null);

public enum AssociationCreationOutcome
{
    Created, InvalidInput, InvalidSlug, SlugExists, AdministratorUnavailable, ConfirmationRequired, Failed, UsernameExists, IdentityRejected
}

public sealed record AssociationCreationResult(AssociationCreationOutcome Outcome, int? AssociationId = null);
