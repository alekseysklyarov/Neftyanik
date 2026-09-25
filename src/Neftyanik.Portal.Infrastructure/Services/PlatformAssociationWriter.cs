using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class PlatformAssociationWriter(
    ApplicationDbContext database,
    IPlatformAssociationWriteAccess access,
    TimeProvider clock,
    ILogger<PlatformAssociationWriter> logger) : IPlatformAssociationWriter
{
    public async Task<PlatformAssociationEditState?> GetAsync(int associationId, CancellationToken cancellationToken = default)
    {
        await access.GetOperatorIdAsync(cancellationToken);
        var association = await database.Associations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == associationId, cancellationToken);
        return association is null ? null : new PlatformAssociationEditState(association.Id, association.Slug,
            association.Name, association.ContactEmail, association.ContactPhone, association.PostalAddress,
            association.IsActive, association.Revision, !IsProtected(association));
    }

    public async Task<AssociationWriteResult> UpdateAsync(int associationId, AssociationMetadataUpdate update, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetOperatorIdAsync(cancellationToken);
        var name = update.Name?.Trim();
        var email = Normalize(update.ContactEmail);
        var phone = Normalize(update.ContactPhone);
        var address = Normalize(update.PostalAddress);
        if (string.IsNullOrEmpty(name) || name.Length > AssociationMetadataLimits.Name)
            return new(AssociationWriteOutcome.InvalidInput, nameof(update.Name));
        if (email is not null && (email.Length > AssociationMetadataLimits.ContactEmail
            || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email))
            return new(AssociationWriteOutcome.InvalidInput, nameof(update.ContactEmail));
        if (phone?.Length > AssociationMetadataLimits.ContactPhone)
            return new(AssociationWriteOutcome.InvalidInput, nameof(update.ContactPhone));
        if (address?.Length > AssociationMetadataLimits.PostalAddress)
            return new(AssociationWriteOutcome.InvalidInput, nameof(update.PostalAddress));

        return await MutateAsync(associationId, update.Revision, actor, PlatformAuditActions.AssociationEdited,
            false, (association, oldValues, newValues) =>
            {
                AddChange(oldValues, newValues, nameof(Association.Name), association.Name, name);
                AddChange(oldValues, newValues, nameof(Association.ContactEmail), association.ContactEmail, email);
                AddChange(oldValues, newValues, nameof(Association.ContactPhone), association.ContactPhone, phone);
                AddChange(oldValues, newValues, nameof(Association.PostalAddress), association.PostalAddress, address);
                association.Name = name;
                association.ContactEmail = email;
                association.ContactPhone = phone;
                association.PostalAddress = address;
            }, cancellationToken);
    }

    public async Task<AssociationWriteResult> SetActiveAsync(int associationId, bool isActive, Guid revision, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetOperatorIdAsync(cancellationToken);
        return await MutateAsync(associationId, revision, actor,
            isActive ? PlatformAuditActions.AssociationActivated : PlatformAuditActions.AssociationDeactivated,
            !isActive, (association, oldValues, newValues) =>
            {
                AddChange(oldValues, newValues, nameof(Association.IsActive), association.IsActive, isActive);
                association.IsActive = isActive;
            }, cancellationToken);
    }

    private async Task<AssociationWriteResult> MutateAsync(int associationId, Guid revision, string actor, string action,
        bool deactivating, Action<Association, Dictionary<string, object?>, Dictionary<string, object?>> change,
        CancellationToken cancellationToken)
    {
        Association? association = null;
        PlatformAuditLog? audit = null;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            association = await database.Associations.SingleOrDefaultAsync(x => x.Id == associationId, cancellationToken);
            if (association is null) return new(AssociationWriteOutcome.NotFound);
            if (deactivating && IsProtected(association)) return new(AssociationWriteOutcome.ProtectedAssociation);

            var oldValues = new Dictionary<string, object?>();
            var newValues = new Dictionary<string, object?>();
            change(association, oldValues, newValues);
            if (newValues.Count == 0) return new(AssociationWriteOutcome.Unchanged);
            if (association.Revision != revision) return new(AssociationWriteOutcome.Conflict);

            association.Revision = Guid.NewGuid();
            audit = new PlatformAuditLog
            {
                AssociationId = association.Id,
                OperatorUserId = actor,
                OccurredAtUtc = clock.GetUtcNow().ToUniversalTime(),
                Action = action,
                OldValuesJson = JsonSerializer.Serialize(oldValues),
                NewValuesJson = JsonSerializer.Serialize(newValues)
            };
            database.PlatformAuditLogs.Add(audit);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(AssociationWriteOutcome.Saved);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(AssociationWriteOutcome.Conflict);
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Platform association change failed for {AssociationId}.", associationId);
            return new(AssociationWriteOutcome.Failed);
        }
        finally
        {
            if (audit is not null) database.Entry(audit).State = EntityState.Detached;
            if (association is not null) database.Entry(association).State = EntityState.Detached;
        }
    }

    private static bool IsProtected(Association association) =>
        string.Equals(association.Slug, "neftyanik", StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddChange<T>(Dictionary<string, object?> oldValues, Dictionary<string, object?> newValues,
        string field, T oldValue, T newValue)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue)) return;
        oldValues.Add(field, oldValue);
        newValues.Add(field, newValue);
    }
}
