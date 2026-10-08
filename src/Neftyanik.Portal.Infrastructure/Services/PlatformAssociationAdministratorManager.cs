using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class PlatformAssociationAdministratorManager(IServiceScopeFactory scopes, IPlatformAssociationWriteAccess access)
{
    // Resolve a separate tenant scope; never switch the association on the platform request itself.
    public async Task<string?> ChangeAsync(int associationId, string userName, bool? active, string? password, CancellationToken ct)
    {
        var actor = await access.GetOperatorIdAsync(ct);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<AssociationContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var association = await db.Associations.SingleOrDefaultAsync(a => a.Id == associationId, ct);
        if (association is null) return "Товарищество не найдено.";
        tenant.Resolve(association);
        await AdvancePaymentAllocator.LockAsync(db, ct);
        var user = await users.FindByNameAsync(userName);
        if (user is null) return "Администратор не найден.";
        var assignment = await db.AssociationUserMemberships.SingleOrDefaultAsync(m => m.ApplicationUserId == user.Id && m.Role == RoleNames.Administrator, ct);
        if (assignment is null) return "Назначение администратора не найдено в этом товариществе.";
        var previousActive = assignment.IsActive;
        if (active.HasValue)
        {
            if (!active.Value && assignment.IsActive)
            {
                var now = DateTimeOffset.UtcNow;
                var otherActive = await db.AssociationUserMemberships.AnyAsync(m => m.Role == RoleNames.Administrator && m.IsActive
                    && m.ApplicationUserId != user.Id && m.ApplicationUser.IsActive
                    && (!m.ApplicationUser.LockoutEnd.HasValue || m.ApplicationUser.LockoutEnd <= now), ct);
                if (!otherActive) return "Нельзя отключить последнего действующего администратора. Сначала назначьте другого.";
            }
            assignment.IsActive = active.Value;
            if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) return "Не удалось обновить доступ.";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length > 128) return "Укажите временный пароль длиной до 128 символов.";
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var result = await users.ResetPasswordAsync(user, token, password);
            if (!result.Succeeded) return "Пароль не соответствует требованиям: минимум 6 символов, буква и цифра.";
            user.MustChangePassword = true;
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;
            if (!(await users.UpdateAsync(user)).Succeeded) return "Не удалось сохранить восстановление доступа.";
        }
        db.PlatformAuditLogs.Add(new PlatformAuditLog
        {
            AssociationId = associationId, OperatorUserId = actor, OccurredAtUtc = DateTimeOffset.UtcNow,
            Action = active.HasValue ? "AdministratorAccessChanged" : "AdministratorPasswordReset",
            OldValuesJson = JsonSerializer.Serialize(new { IsActive = previousActive }), NewValuesJson = JsonSerializer.Serialize(new { AdministratorUserName = user.UserName, IsActive = assignment.IsActive })
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return null;
    }
}
