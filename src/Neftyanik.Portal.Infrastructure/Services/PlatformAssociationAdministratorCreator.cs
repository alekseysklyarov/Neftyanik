using System.Data;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class PlatformAssociationAdministratorCreator(IServiceScopeFactory scopes,
    IPlatformAssociationWriteAccess access) : IPlatformAssociationAdministratorCreator
{
    public async Task<AdministratorCreationOutcome> CreateAsync(int associationId, AssociationAdministratorRequest request,
        CancellationToken cancellationToken = default)
    {
        var actor = await access.GetOperatorIdAsync(cancellationToken);
        if (!request.ConfirmAssignment) return AdministratorCreationOutcome.ConfirmationRequired;
        request = request with
        {
            UserName = request.UserName?.Trim() ?? string.Empty,
            Email = request.Email?.Trim() ?? string.Empty,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim()
        };
        if (request.UserName.Length is 0 or > 256 || request.Email.Length is 0 or > 256
            || !MailAddress.TryCreate(request.Email, out var email) || email.Address != request.Email
            || request.DisplayName?.Length > 200 || string.IsNullOrWhiteSpace(request.TemporaryPassword)
            || request.TemporaryPassword.Length > 128)
            return AdministratorCreationOutcome.InvalidInput;

        // Only the isolated provisioning scope resolves the association; the platform request stays tenantless.
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AssociationAdministratorCreationOperation>()
            .CreateAsync(associationId, request, actor, cancellationToken);
    }
}

internal sealed class AssociationAdministratorCreationOperation(ApplicationDbContext database, AssociationContext tenant,
    UserManager<ApplicationUser> users, TimeProvider clock, ILogger<AssociationAdministratorCreationOperation> logger)
{
    public async Task<AdministratorCreationOutcome> CreateAsync(int associationId, AssociationAdministratorRequest request,
        string actor, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(
                database.Database.IsSqlServer() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, cancellationToken);
            var association = await database.Associations.SingleOrDefaultAsync(x => x.Id == associationId, cancellationToken);
            if (association is null) return AdministratorCreationOutcome.NotFound;
            if (!association.IsActive) return AdministratorCreationOutcome.InactiveAssociation;
            if (await users.FindByNameAsync(request.UserName) is not null) return AdministratorCreationOutcome.UsernameExists;
            tenant.Resolve(association);
            var user = new ApplicationUser
            {
                UserName = request.UserName, Email = request.Email, DisplayName = request.DisplayName,
                IsActive = true, MustChangePassword = true, LockoutEnabled = true, CreatedAt = clock.GetUtcNow()
            };
            var result = await users.CreateAsync(user, request.TemporaryPassword);
            if (!result.Succeeded)
                return result.Errors.Any(x => x.Code == "DuplicateUserName")
                    ? AdministratorCreationOutcome.UsernameExists : AdministratorCreationOutcome.IdentityRejected;
            database.AssociationUserMemberships.Add(new AssociationUserMembership
            {
                ApplicationUserId = user.Id, Role = RoleNames.Administrator, IsActive = true, CreatedAtUtc = clock.GetUtcNow()
            });
            database.PlatformAuditLogs.Add(new PlatformAuditLog
            {
                AssociationId = associationId, OperatorUserId = actor, OccurredAtUtc = clock.GetUtcNow(),
                Action = PlatformAuditActions.AdministratorAssigned, OldValuesJson = "{}",
                NewValuesJson = JsonSerializer.Serialize(new
                {
                    AdministratorUserId = user.Id, AdministratorUserName = user.UserName, Role = RoleNames.Administrator
                })
            });
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdministratorCreationOutcome.Created;
        }
        catch (OperationCanceledException) { throw; }
        catch (DbUpdateException exception)
        {
            database.ChangeTracker.Clear();
            if (await users.FindByNameAsync(request.UserName) is not null) return AdministratorCreationOutcome.UsernameExists;
            logger.LogError(exception, "Association administrator provisioning could not be saved.");
            return AdministratorCreationOutcome.Failed;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Association administrator provisioning failed.");
            return AdministratorCreationOutcome.Failed;
        }
    }
}
