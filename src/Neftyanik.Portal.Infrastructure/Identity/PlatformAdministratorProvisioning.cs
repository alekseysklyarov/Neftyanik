using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Identity;

internal static class PlatformAdministratorProvisioning
{
    internal static bool IsValid(string login, string email, string password) =>
        !string.IsNullOrWhiteSpace(login) && login.Trim().Length <= 256
        && !string.IsNullOrWhiteSpace(email) && email.Trim().Length <= 256
        && new EmailAddressAttribute().IsValid(email.Trim()) && !string.IsNullOrEmpty(password);

    internal static async Task<bool> AcquireLockAsync(ApplicationDbContext database, IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = N'DachaHub.FirstPlatformAdministrator', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000; SELECT @result;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) >= 0;
    }

    internal static async Task<bool> HasEvidenceAsync(ApplicationDbContext database, CancellationToken cancellationToken) =>
        await database.Roles.AsNoTracking().AnyAsync(x => x.Name == RoleNames.PlatformAdministrator || x.NormalizedName == "PLATFORMADMINISTRATOR", cancellationToken)
        || await database.RoleClaims.AsNoTracking().AnyAsync(x => x.ClaimType == PlatformAdministratorOnboarding.BootstrapMarker
            || x.ClaimValue == RoleNames.PlatformAdministrator, cancellationToken)
        || await database.UserClaims.AsNoTracking().AnyAsync(x => x.ClaimType == PlatformAdministratorOnboarding.BootstrapMarker
            || x.ClaimValue == RoleNames.PlatformAdministrator, cancellationToken)
        || await database.UserTokens.AsNoTracking().AnyAsync(x => x.LoginProvider == PlatformAdministratorOnboarding.TokenProvider, cancellationToken);

    internal static Task<bool> HasCollisionAsync(ApplicationDbContext database, UserManager<ApplicationUser> users, string login, string email, CancellationToken cancellationToken)
    {
        var normalizedName = users.NormalizeName(login.Trim());
        var normalizedEmail = users.NormalizeEmail(email.Trim());
        var normalizedLoginEmail = users.NormalizeEmail(login.Trim());
        var normalizedEmailName = users.NormalizeName(email.Trim());
        return database.Users.AsNoTracking().AnyAsync(x => x.NormalizedUserName == normalizedName || x.NormalizedEmail == normalizedEmail
            || x.NormalizedEmail == normalizedLoginEmail || x.NormalizedUserName == normalizedEmailName, cancellationToken);
    }

    internal static async Task<(PlatformBootstrapResult Result, ApplicationUser? User)> CreateAsync(
        UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, TimeProvider clock,
        string login, string email, string temporaryPassword)
    {
        var user = new ApplicationUser
        {
            UserName = login.Trim(), FirstName = "Platform", LastName = "Administrator",
            Email = email.Trim(), EmailConfirmed = false,
            IsActive = true, MustChangePassword = true, LockoutEnabled = true, TwoFactorEnabled = false
        };
        if (!(await users.CreateAsync(user, temporaryPassword)).Succeeded) return (PlatformBootstrapResult.InvalidInput, null);
        if (!(await roles.CreateAsync(new IdentityRole(RoleNames.PlatformAdministrator))).Succeeded
            || !(await users.AddToRoleAsync(user, RoleNames.PlatformAdministrator)).Succeeded
            || !(await users.SetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider,
                PlatformAdministratorOnboarding.ExpiryToken, clock.GetUtcNow().AddHours(24).ToString("O", CultureInfo.InvariantCulture))).Succeeded
            || !(await users.SetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider,
                PlatformAdministratorOnboarding.CliAuthorizationStamp, user.SecurityStamp!)).Succeeded)
            return (PlatformBootstrapResult.Failed, null);
        return (PlatformBootstrapResult.Created, user);
    }
}
