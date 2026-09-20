using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Identity;

public sealed class PlatformAccountRecovery(
    ApplicationDbContext database,
    UserManager<ApplicationUser> users,
    IPlatformEmailSender emailSender,
    IOptions<PlatformRecoveryOptions> options,
    ILogger<PlatformAccountRecovery> logger) : IPlatformAccountRecovery
{
    public async Task RequestAsync(string email, bool confirmEmail, CancellationToken cancellationToken = default)
    {
        if (database.IsAssociationResolved || string.IsNullOrWhiteSpace(email) || email.Length > 256) return;
        try
        {
            var normalized = users.NormalizeEmail(email.Trim());
            var matches = await database.Users.AsNoTracking().Where(x => x.NormalizedEmail == normalized).Take(2).ToListAsync(cancellationToken);
            if (matches.Count != 1) return;
            var user = matches[0];
            if (!await EligibleAsync(user) || user.EmailConfirmed == confirmEmail) return;
            var origin = options.Value.GetOrigin();
            var token = confirmEmail
                ? await users.GenerateEmailConfirmationTokenAsync(user)
                : await users.GenerateUserTokenAsync(user, PlatformRecoveryTokenProvider.ProviderName, PlatformRecoveryTokenProvider.ResetPurpose);
            var page = confirmEmail ? "ConfirmEmail" : "ResetPassword";
            // Fragments are never sent in HTTP requests, proxy access logs or Referer headers.
            var link = new Uri(origin, $"Platform/Account/{page}").AbsoluteUri
                + "#userId=" + Uri.EscapeDataString(user.Id) + "&token=" + Uri.EscapeDataString(token);
            await emailSender.SendAsync(user.Email!, confirmEmail ? "DachaHub: подтверждение email" : "DachaHub: восстановление пароля",
                $"Откройте ссылку, чтобы {(confirmEmail ? "подтвердить email" : "установить новый пароль")}:\n{link}\n\nЕсли вы не запрашивали это действие, проигнорируйте письмо. Ссылка восстановления пароля действует 15 минут.", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // Transport exceptions can contain message content or credentials; never log them.
            logger.LogWarning("Platform account email delivery could not be completed.");
        }
    }

    public async Task<bool> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken = default)
    {
        if (database.IsAssociationResolved || !database.Database.IsRelational()) return false;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var user = await database.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
            if (user is null || user.EmailConfirmed || !await EligibleAsync(user) || !await UniqueEmailAsync(user, cancellationToken)) return false;
            if (!(await users.ConfirmEmailAsync(user, token)).Succeeded) return false;
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { logger.LogWarning("Platform email confirmation failed."); return false; }
        finally { database.ChangeTracker.Clear(); }
    }

    public async Task<bool> ResetPasswordAsync(string userId, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        if (database.IsAssociationResolved || !database.Database.IsRelational()) return false;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var user = await database.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
            if (user is null || !user.EmailConfirmed || !await EligibleAsync(user) || !await UniqueEmailAsync(user, cancellationToken)
                || !await users.VerifyUserTokenAsync(user, PlatformRecoveryTokenProvider.ProviderName, PlatformRecoveryTokenProvider.ResetPurpose, token)) return false;
            // Keep tenant Identity providers unchanged. The external token uses only the dedicated provider.
            var identityToken = await users.GeneratePasswordResetTokenAsync(user);
            if (!(await users.ResetPasswordAsync(user, identityToken, newPassword)).Succeeded) return false;
            // Mailbox proof plus a newly chosen password completes initial-password onboarding.
            user.MustChangePassword = false;
            if (!(await users.UpdateAsync(user)).Succeeded
                || !(await users.RemoveAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider, PlatformAdministratorOnboarding.ExpiryToken)).Succeeded
                || !(await users.RemoveAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider, PlatformAdministratorOnboarding.CliAuthorizationStamp)).Succeeded) return false;
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { logger.LogWarning("Platform password recovery failed."); return false; }
        finally { database.ChangeTracker.Clear(); }
    }

    private async Task<bool> EligibleAsync(ApplicationUser user) =>
        user.IsActive && !user.TwoFactorEnabled && !string.IsNullOrWhiteSpace(user.Email)
        && !await users.IsLockedOutAsync(user) && await users.IsInRoleAsync(user, RoleNames.PlatformAdministrator);

    private Task<bool> UniqueEmailAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        database.Users.AllAsync(x => x.Id == user.Id || x.NormalizedEmail != user.NormalizedEmail, cancellationToken);
}
