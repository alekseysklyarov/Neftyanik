using System.Data;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Identity;

public sealed class PlatformAdministratorOnboarding(
    ApplicationDbContext database,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    TimeProvider clock) : IPlatformAdministratorOnboarding
{
    public const string BootstrapMarker = "dachahub:first-platform-administrator-provisioned";
    public const string TokenProvider = "DachaHub.PlatformOnboarding";
    public const string ExpiryToken = "TemporaryPasswordExpiresUtc";

    public async Task<PlatformBootstrapResult> BootstrapAsync(string login, string email, string temporaryPassword, CancellationToken cancellationToken = default)
    {
        if (!PlatformAdministratorProvisioning.IsValid(login, email, temporaryPassword))
        {
            return PlatformBootstrapResult.InvalidInput;
        }
        if (!database.Database.IsRelational() || database.IsAssociationResolved)
        {
            return PlatformBootstrapResult.Failed;
        }

        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (database.Database.IsSqlServer()
                && !await PlatformAdministratorProvisioning.AcquireLockAsync(database, transaction, cancellationToken))
                return PlatformBootstrapResult.Failed;

            if (await database.PlatformBootstrapStates.AnyAsync(cancellationToken))
            {
                return PlatformBootstrapResult.AlreadyProvisioned;
            }
            if (await PlatformAdministratorProvisioning.HasEvidenceAsync(database, cancellationToken))
            {
                database.PlatformBootstrapStates.Add(new PlatformBootstrapState { Id = 1, ConsumedAtUtc = clock.GetUtcNow(), Reason = "Legacy or ambiguous platform state" });
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return PlatformBootstrapResult.AlreadyProvisioned;
            }
            if (await PlatformAdministratorProvisioning.HasCollisionAsync(database, users, login, email, cancellationToken))
            {
                return PlatformBootstrapResult.AccountExists;
            }
            if (await database.Users.AnyAsync(cancellationToken))
            {
                database.PlatformBootstrapStates.Add(new PlatformBootstrapState { Id = 1, ConsumedAtUtc = clock.GetUtcNow(), Reason = "Non-pristine store without migration classification" });
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return PlatformBootstrapResult.AlreadyProvisioned;
            }
            var creation = await PlatformAdministratorProvisioning.CreateAsync(users, roles, clock, login, email, temporaryPassword);
            if (creation.Result != PlatformBootstrapResult.Created) return creation.Result;
            database.PlatformBootstrapStates.Add(new PlatformBootstrapState
            {
                Id = 1, Disposition = PlatformBootstrapDisposition.Consumed, ConsumedAtUtc = clock.GetUtcNow(),
                Reason = "First administrator provisioned", InitializedAtUtc = clock.GetUtcNow(), InitializedUserId = creation.User!.Id
            });
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PlatformBootstrapResult.Created;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Do not expose Identity errors, SQL details or supplied credentials.
            return PlatformBootstrapResult.Failed;
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    public async Task<bool> CanChangePasswordAsync(string userId, string securityStamp, CancellationToken cancellationToken = default)
    {
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        return await IsPendingAsync(user, securityStamp);
    }

    private async Task<bool> IsPendingAsync(ApplicationUser? user, string securityStamp)
    {
        if (user is not { IsActive: true, MustChangePassword: true, TwoFactorEnabled: false }
            || string.IsNullOrEmpty(securityStamp) || user.SecurityStamp != securityStamp
            || await users.IsLockedOutAsync(user) || !await users.IsInRoleAsync(user, RoleNames.PlatformAdministrator))
        {
            return false;
        }
        var expiry = await users.GetAuthenticationTokenAsync(user, TokenProvider, ExpiryToken);
        return DateTimeOffset.TryParseExact(expiry, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresAt)
            && expiresAt > clock.GetUtcNow();
    }

    public async Task<PlatformPasswordChangeResult> ChangePasswordAsync(string userId, string securityStamp, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(newPassword) || string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return PlatformPasswordChangeResult.InvalidPassword;
        }
        if (!database.Database.IsRelational() || database.IsAssociationResolved) return PlatformPasswordChangeResult.Denied;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var user = await database.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
            if (!await IsPendingAsync(user, securityStamp)) return PlatformPasswordChangeResult.Denied;
            if (!(await users.ChangePasswordAsync(user!, currentPassword, newPassword)).Succeeded)
            {
                return PlatformPasswordChangeResult.InvalidPassword;
            }
            user!.MustChangePassword = false;
            if (!(await users.UpdateAsync(user)).Succeeded
                || !(await users.RemoveAuthenticationTokenAsync(user, TokenProvider, ExpiryToken)).Succeeded)
            {
                return PlatformPasswordChangeResult.Failed;
            }
            await transaction.CommitAsync(cancellationToken);
            return PlatformPasswordChangeResult.Succeeded;
        }
        catch (OperationCanceledException) { throw; }
        catch { return PlatformPasswordChangeResult.Failed; }
        finally { database.ChangeTracker.Clear(); }
    }
}
