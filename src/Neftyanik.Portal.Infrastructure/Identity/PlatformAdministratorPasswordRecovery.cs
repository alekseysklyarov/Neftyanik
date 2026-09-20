using System.Data;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Identity;

public sealed class PlatformAdministratorPasswordRecovery(
    ApplicationDbContext database,
    UserManager<ApplicationUser> users,
    TimeProvider clock) : IPlatformAdministratorPasswordRecovery
{
    public async Task<string?> GetSecurityStampAsync(string exactLogin, CancellationToken cancellationToken = default)
    {
        if (!database.Database.IsSqlServer() || database.IsAssociationResolved || !ValidLogin(exactLogin)) return null;
        var user = await FindExactAsync(exactLogin, cancellationToken);
        return user is not null && await EligibleAsync(user) ? user.SecurityStamp : null;
    }

    public async Task<PlatformOperatorPasswordResetResult> ResetAsync(string exactLogin, string expectedSecurityStamp,
        string temporaryPassword, string operatorIdentity, string approvalReference, CancellationToken cancellationToken = default)
    {
        if (!ValidLogin(exactLogin) || string.IsNullOrEmpty(expectedSecurityStamp) || string.IsNullOrEmpty(temporaryPassword)
            || !ValidAuditValue(operatorIdentity, 256) || !ValidAuditValue(approvalReference, 100))
            return PlatformOperatorPasswordResetResult.InvalidInput;
        if (!database.Database.IsSqlServer() || database.IsAssociationResolved) return PlatformOperatorPasswordResetResult.Denied;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (!await PlatformAdministratorProvisioning.AcquireLockAsync(database, transaction, cancellationToken))
                return PlatformOperatorPasswordResetResult.Failed;
            var user = await FindExactAsync(exactLogin, cancellationToken, track: true);
            if (user is null || !await EligibleAsync(user)) return PlatformOperatorPasswordResetResult.Denied;
            if (!string.Equals(user.SecurityStamp, expectedSecurityStamp, StringComparison.Ordinal))
                return PlatformOperatorPasswordResetResult.Conflict;
            var token = await users.GeneratePasswordResetTokenAsync(user);
            if (!(await users.ResetPasswordAsync(user, token, temporaryPassword)).Succeeded)
                return PlatformOperatorPasswordResetResult.InvalidPassword;
            user.MustChangePassword = true;
            if (!(await users.UpdateAsync(user)).Succeeded
                || !(await users.SetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider,
                    PlatformAdministratorOnboarding.ExpiryToken, clock.GetUtcNow().AddHours(24).ToString("O", CultureInfo.InvariantCulture))).Succeeded
                || !(await users.SetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider,
                    PlatformAdministratorOnboarding.CliAuthorizationStamp, user.SecurityStamp!)).Succeeded)
                return PlatformOperatorPasswordResetResult.Failed;
            database.PlatformPasswordRecoveryAudits.Add(new PlatformPasswordRecoveryAudit
            {
                UserId = user.Id, OperatorIdentity = operatorIdentity.Trim(), ApprovalReference = approvalReference.Trim(),
                OccurredAtUtc = clock.GetUtcNow()
            });
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PlatformOperatorPasswordResetResult.Succeeded;
        }
        catch (OperationCanceledException) { throw; }
        catch { return PlatformOperatorPasswordResetResult.Failed; }
        finally { database.ChangeTracker.Clear(); }
    }

    private async Task<ApplicationUser?> FindExactAsync(string login, CancellationToken cancellationToken, bool track = false)
    {
        var normalizedName = users.NormalizeName(login);
        var normalizedEmail = users.NormalizeEmail(login);
        var query = track ? database.Users.AsTracking() : database.Users.AsNoTracking();
        var matches = await query.Where(x => x.NormalizedUserName == normalizedName || x.NormalizedEmail == normalizedEmail)
            .Take(2).ToListAsync(cancellationToken);
        if (track && matches.Count == 1) await database.Entry(matches[0]).ReloadAsync(cancellationToken);
        return matches.Count == 1 && string.Equals(matches[0].UserName, login, StringComparison.Ordinal) ? matches[0] : null;
    }

    private async Task<bool> EligibleAsync(ApplicationUser user) =>
        user.IsActive && !user.TwoFactorEnabled && !string.IsNullOrEmpty(user.SecurityStamp)
        && !await users.IsLockedOutAsync(user) && await users.IsInRoleAsync(user, RoleNames.PlatformAdministrator);

    private static bool ValidLogin(string login) =>
        !string.IsNullOrWhiteSpace(login) && login.Length <= 256 && !login.Any(char.IsControl);

    private static bool ValidAuditValue(string value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= limit && !value.Any(char.IsControl);
}
