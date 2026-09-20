using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Identity;

public sealed class PlatformLegacyInitialization(
    ApplicationDbContext database,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    TimeProvider clock) : IPlatformLegacyInitialization
{
    public async Task<PlatformBootstrapResult> InitializeAsync(string login, string email, string temporaryPassword,
        string operatorIdentity, bool ownerConfirmed, CancellationToken cancellationToken = default)
    {
        if (!ownerConfirmed || !PlatformAdministratorProvisioning.IsValid(login, email, temporaryPassword)
            || !ValidAuditValue(operatorIdentity, 256))
            return PlatformBootstrapResult.InvalidInput;
        if (!database.Database.IsSqlServer() || database.IsAssociationResolved) return PlatformBootstrapResult.Failed;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (!await PlatformAdministratorProvisioning.AcquireLockAsync(database, transaction, cancellationToken))
                return PlatformBootstrapResult.Failed;
            var markers = await database.PlatformBootstrapStates.Take(2).ToListAsync(cancellationToken);
            if (markers.Count != 1) return PlatformBootstrapResult.AlreadyProvisioned;
            var marker = markers[0];
            if (marker.Id != 1 || marker.Disposition != PlatformBootstrapDisposition.LegacyReviewRequired
                || marker.ConsumedAtUtc == default || marker.InitializedAtUtc is not null || marker.InitializedUserId is not null
                || marker.OperatorIdentity is not null || marker.ApprovalReference is not null)
                return PlatformBootstrapResult.AlreadyProvisioned;
            if (await PlatformAdministratorProvisioning.HasEvidenceAsync(database, cancellationToken))
            {
                // Retain observed evidence permanently even if its Identity records are later deleted.
                marker.Disposition = PlatformBootstrapDisposition.Consumed;
                marker.Reason = "Platform evidence detected during legacy initialization";
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return PlatformBootstrapResult.AlreadyProvisioned;
            }
            if (!await database.Users.AnyAsync(cancellationToken)) return PlatformBootstrapResult.AlreadyProvisioned;
            if (await PlatformAdministratorProvisioning.HasCollisionAsync(database, users, login, email, cancellationToken))
                return PlatformBootstrapResult.AccountExists;
            var creation = await PlatformAdministratorProvisioning.CreateAsync(users, roles, clock, login, email, temporaryPassword);
            if (creation.Result != PlatformBootstrapResult.Created) return creation.Result;
            marker.Disposition = PlatformBootstrapDisposition.Consumed;
            marker.ConsumedAtUtc = clock.GetUtcNow();
            marker.Reason = "Owner attestation: first PlatformAdministrator; creation authorized";
            marker.OperatorIdentity = operatorIdentity.Trim();
            marker.ApprovalReference = null;
            marker.InitializedAtUtc = clock.GetUtcNow();
            marker.InitializedUserId = creation.User!.Id;
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PlatformBootstrapResult.Created;
        }
        catch (OperationCanceledException) { throw; }
        catch { return PlatformBootstrapResult.Failed; }
        finally { database.ChangeTracker.Clear(); }
    }

    private static bool ValidAuditValue(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximumLength && !value.Any(char.IsControl);
}
