using System.Net.Mail;
using Microsoft.Extensions.DependencyInjection;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class PlatformAssociationCreator(
    IServiceScopeFactory scopes,
    IPlatformAssociationWriteAccess access,
    IAssociationSlugReservations reservations) : IPlatformAssociationCreator
{
    public async Task<AssociationCreationResult> CreateAsync(AssociationCreationRequest request, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetOperatorIdAsync(cancellationToken);
        if (!request.ConfirmAdministrator) return new(AssociationCreationOutcome.ConfirmationRequired);
        request = request with
        {
            Name = request.Name?.Trim() ?? string.Empty,
            Slug = request.Slug?.Trim() ?? string.Empty,
            AdministratorUserName = request.AdministratorUserName?.Trim() ?? string.Empty,
            AdministratorEmail = request.AdministratorEmail?.Trim() ?? string.Empty,
            AdministratorDisplayName = Normalize(request.AdministratorDisplayName),
            ContactEmail = Normalize(request.ContactEmail), ContactPhone = Normalize(request.ContactPhone),
            PostalAddress = Normalize(request.PostalAddress)
        };
        if (request.AdministratorUserName.Length is 0 or > 256
            || request.AdministratorEmail.Length is 0 or > 256
            || !MailAddress.TryCreate(request.AdministratorEmail, out var adminEmail) || adminEmail.Address != request.AdministratorEmail
            || request.AdministratorDisplayName?.Length > 200
            || string.IsNullOrWhiteSpace(request.TemporaryPassword) || request.TemporaryPassword.Length > 128
            || request.Name.Length == 0 || request.Name.Length > AssociationMetadataLimits.Name
            || request.ContactPhone?.Length > AssociationMetadataLimits.ContactPhone
            || request.PostalAddress?.Length > AssociationMetadataLimits.PostalAddress
            || (request.ContactEmail is not null && (request.ContactEmail.Length > AssociationMetadataLimits.ContactEmail
                || !MailAddress.TryCreate(request.ContactEmail, out var email) || email.Address != request.ContactEmail)))
            return new(AssociationCreationOutcome.InvalidInput);
        try { new Association { Slug = request.Slug }.ValidateSlug(); }
        catch (InvalidOperationException) { return new(AssociationCreationOutcome.InvalidSlug); }
        if (reservations.IsReserved(request.Slug)) return new(AssociationCreationOutcome.InvalidSlug);

        // The HTTP request remains tenantless; this scope owns the entire provisioning transaction.
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AssociationCreationOperation>()
            .CreateAsync(request, actor, cancellationToken);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
