using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;

namespace Neftyanik.Portal.Infrastructure.Services;

internal sealed class AssociationCreationOperation(
    ApplicationDbContext database,
    AssociationContext tenant,
    UserManager<ApplicationUser> users,
    TimeProvider clock,
    ILogger<AssociationCreationOperation> logger)
{
    public async Task<AssociationCreationResult> CreateAsync(AssociationCreationRequest request, string actor, CancellationToken cancellationToken)
    {
        try
        {
            if (await database.Associations.AsNoTracking().AnyAsync(x => x.Slug == request.Slug, cancellationToken))
                return new(AssociationCreationOutcome.SlugExists);

            await using var transaction = await database.Database.BeginTransactionAsync(
                database.Database.IsSqlServer() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, cancellationToken);
            if (await users.FindByNameAsync(request.AdministratorUserName) is not null)
                return new(AssociationCreationOutcome.UsernameExists);

            var association = new Association
            {
                Name = request.Name, Slug = request.Slug, ContactEmail = request.ContactEmail,
                ContactPhone = request.ContactPhone, PostalAddress = request.PostalAddress,
                IsActive = true, CreatedAtUtc = clock.GetUtcNow(), Revision = Guid.NewGuid()
            };
            database.Associations.Add(association);
            await database.SaveChangesAsync(cancellationToken);
            tenant.Resolve(association);

            var user = new ApplicationUser
            {
                UserName = request.AdministratorUserName, Email = request.AdministratorEmail,
                DisplayName = request.AdministratorDisplayName, IsActive = true, MustChangePassword = true,
                LockoutEnabled = true, CreatedAt = clock.GetUtcNow()
            };
            var creation = await users.CreateAsync(user, request.TemporaryPassword);
            if (!creation.Succeeded)
                return new(creation.Errors.Any(x => x.Code == "DuplicateUserName")
                    ? AssociationCreationOutcome.UsernameExists : AssociationCreationOutcome.IdentityRejected);

            database.AssociationUserMemberships.Add(new AssociationUserMembership
            {
                ApplicationUserId = user.Id, Role = RoleNames.Administrator,
                IsActive = true, CreatedAtUtc = clock.GetUtcNow()
            });
            await database.SaveChangesAsync(cancellationToken);

            var electricity = new ExpenseCategory { Name = "Електроенергія", IsActive = true };
            database.ExpenseCategories.Add(electricity);
            await database.SaveChangesAsync(cancellationToken);
            database.SystemSettings.Add(new SystemSetting
            {
                Key = ElectricityExpenseCategoryQueries.SettingKey,
                Value = electricity.Id.ToString(CultureInfo.InvariantCulture), UpdatedByUserId = actor
            });
            database.PlatformAuditLogs.Add(new PlatformAuditLog
            {
                AssociationId = association.Id, OperatorUserId = actor, OccurredAtUtc = clock.GetUtcNow().ToUniversalTime(),
                Action = PlatformAuditActions.AssociationCreated, OldValuesJson = "{}",
                NewValuesJson = JsonSerializer.Serialize(new
                {
                    association.Name, association.Slug, association.ContactEmail, association.ContactPhone, association.PostalAddress,
                    AdministratorUserId = user.Id, AdministratorUserName = user.UserName, Role = RoleNames.Administrator
                })
            });
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(AssociationCreationOutcome.Created, association.Id);
        }
        catch (OperationCanceledException) { throw; }
        catch (DbUpdateException exception)
        {
            // After rollback, the unique slug index may have elected another concurrent request.
            database.ChangeTracker.Clear();
            if (await database.Associations.AsNoTracking().AnyAsync(x => x.Slug == request.Slug, cancellationToken))
                return new(AssociationCreationOutcome.SlugExists);
            if (await users.FindByNameAsync(request.AdministratorUserName) is not null)
                return new(AssociationCreationOutcome.UsernameExists);
            logger.LogError(exception, "Association provisioning could not be saved.");
            return new(AssociationCreationOutcome.Failed);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Association provisioning failed.");
            return new(AssociationCreationOutcome.Failed);
        }
    }

}
