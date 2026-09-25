using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data.Configurations;
using Neftyanik.Portal.Application.Associations;
using System.Linq.Expressions;

namespace Neftyanik.Portal.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly IAssociationContext _associationContext;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IAssociationContext? associationContext = null)
        : base(options)
    {
        _associationContext = associationContext ?? new AssociationContext();
    }

    public int CurrentAssociationId => _associationContext.AssociationId;
    public bool IsAssociationResolved => _associationContext.IsResolved;

    public DbSet<PlatformBootstrapState> PlatformBootstrapStates => Set<PlatformBootstrapState>();
    public DbSet<PlatformPasswordRecoveryAudit> PlatformPasswordRecoveryAudits => Set<PlatformPasswordRecoveryAudit>();
    public DbSet<PlatformAuditLog> PlatformAuditLogs => Set<PlatformAuditLog>();

    public DbSet<Association> Associations => Set<Association>();

    public DbSet<AssociationAccountBinding> AssociationAccountBindings => Set<AssociationAccountBinding>();

    public DbSet<AssociationUserMembership> AssociationUserMemberships => Set<AssociationUserMembership>();
    public DbSet<AssociationLoginEvent> AssociationLoginEvents => Set<AssociationLoginEvent>();

    public DbSet<Plot> Plots => Set<Plot>();

    public DbSet<Member> Members => Set<Member>();

    public DbSet<PlotOwnership> PlotOwnerships => Set<PlotOwnership>();

    public DbSet<PlotOwnershipHistory> PlotOwnershipHistories => Set<PlotOwnershipHistory>();

    public DbSet<AssociationElectricityReading> AssociationElectricityReadings => Set<AssociationElectricityReading>();

    public DbSet<AssociationElectricityTariff> AssociationElectricityTariffs => Set<AssociationElectricityTariff>();

    public DbSet<MemberElectricityMeter> MemberElectricityMeters => Set<MemberElectricityMeter>();

    public DbSet<MemberElectricityReading> MemberElectricityReadings => Set<MemberElectricityReading>();

    public DbSet<MemberElectricityTariff> MemberElectricityTariffs => Set<MemberElectricityTariff>();

    public DbSet<MembershipFeeRate> MembershipFeeRates => Set<MembershipFeeRate>();

    public DbSet<ChargeType> ChargeTypes => Set<ChargeType>();

    public DbSet<Charge> Charges => Set<Charge>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentNotification> PaymentNotifications => Set<PaymentNotification>();

    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();

    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();

    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<NewsArticle> NewsArticles => Set<NewsArticle>();

    public DbSet<AssociationDocument> AssociationDocuments => Set<AssociationDocument>();

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<FinancialAuditLog> FinancialAuditLogs => Set<FinancialAuditLog>();

public DbSet<UserLoginHistory> UserLoginHistories => Set<UserLoginHistory>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (ChangeTracker.Entries().Any(x => (x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            && (x.Entity is IAssociationOwned or AssociationAccountBinding or IdentityUserRole<string>)))
        {
            throw new AssociationIsolationException("Use SaveChangesAsync for association-owned writes so stored ownership can be verified.");
        }
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        // Assign before navigation fix-up can propagate an unassigned meter key to an existing plot.
        var autoDetectChanges = ChangeTracker.AutoDetectChangesEnabled;
        ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            EnforceAssociationOwnership();
            ChangeTracker.DetectChanges();
            EnforceAssociationOwnership();

            foreach (var entry in ChangeTracker.Entries<IAssociationOwned>()
                         .Where(x => x.State is EntityState.Modified or EntityState.Deleted))
            {
                // OriginalValues on an attached form/stub are not proof of database ownership.
                var stored = await entry.GetDatabaseValuesAsync(cancellationToken);
                if (stored is null || stored.GetValue<int>(nameof(IAssociationOwned.AssociationId)) != CurrentAssociationId)
                {
                    throw new AssociationIsolationException("The record does not belong to the current association.");
                }
            }
            foreach (var entry in ChangeTracker.Entries<Association>().Where(x => x.State != EntityState.Deleted))
            {
                entry.Entity.ValidateSlug();
            }
            var assignmentUserIds = GetAssignmentUserIds();
            await using var bindingTransaction = Database.IsSqlServer() && assignmentUserIds.Length != 0
                && Database.CurrentTransaction is null
                ? await Database.BeginTransactionAsync(cancellationToken) : null;
            if (Database.IsSqlServer())
                await LockAccountAssignmentsAsync(assignmentUserIds, cancellationToken);
            await EnforceAccountBindingsAsync(cancellationToken);
            ChangeTracker.DetectChanges();
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (bindingTransaction is not null) await bindingTransaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }

    private string[] GetAssignmentUserIds()
    {
        if (Model.FindEntityType(typeof(AssociationAccountBinding)) is null) return [];
        return ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(x => x.Entity switch
            {
                AssociationAccountBinding binding => binding.ApplicationUserId,
                AssociationUserMembership membership => membership.ApplicationUserId,
                Member member => member.ApplicationUserId,
                IdentityUserRole<string> role => role.UserId,
                _ => null
            }).OfType<string>().Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private async Task LockAccountAssignmentsAsync(string[] userIds, CancellationToken cancellationToken)
    {
        // Both Identity role writes and tenant bindings use this transaction-owned lock.
        // It must be acquired before reading the competing assignment and held through commit.
        foreach (var userId in userIds)
        {
            var resource = "DachaHub.AccountAssignment:" + Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(userId)));
            await Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction', @LockTimeout = 15000;
                IF @result < 0 THROW 51022, 'Account assignment lock could not be acquired.', 1;
                """, cancellationToken);
        }
    }

    private async Task EnforceAccountBindingsAsync(CancellationToken cancellationToken)
    {
        if (Model.FindEntityType(typeof(AssociationAccountBinding)) is null) return;
        if (!ChangeTracker.Entries().Any(x => (x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            && (x.Entity is AssociationUserMembership or Member or AssociationAccountBinding or IdentityUserRole<string>))) return;
        if (ChangeTracker.Entries<AssociationAccountBinding>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new AssociationIsolationException("An account's association binding is permanent.");

        var platformRoleIds = await Roles.AsNoTracking()
            .Where(x => x.NormalizedName == "PLATFORMADMINISTRATOR").Select(x => x.Id).ToListAsync(cancellationToken);
        platformRoleIds.AddRange(ChangeTracker.Entries<IdentityRole>()
            .Where(x => x.Entity.NormalizedName == "PLATFORMADMINISTRATOR").Select(x => x.Entity.Id));
        var pendingPlatformUsers = ChangeTracker.Entries<IdentityUserRole<string>>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified && platformRoleIds.Contains(x.Entity.RoleId))
            .Select(x => x.Entity.UserId).ToHashSet();
        var targets = ChangeTracker.Entries<AssociationUserMembership>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified)
            .Select(x => (UserId: x.Entity.ApplicationUserId, x.Entity.AssociationId))
            .Concat(ChangeTracker.Entries<Member>().Where(x => x.State is EntityState.Added or EntityState.Modified
                && x.Entity.ApplicationUserId != null).Select(x => (x.Entity.ApplicationUserId!, x.Entity.AssociationId)))
            .Concat(ChangeTracker.Entries<AssociationAccountBinding>().Where(x => x.State == EntityState.Added)
                .Select(x => (x.Entity.ApplicationUserId, x.Entity.AssociationId))).Distinct().ToArray();
        foreach (var (userId, associationId) in targets)
        {
            if (!IsAssociationResolved || associationId != CurrentAssociationId
                || pendingPlatformUsers.Contains(userId)
                || await UserRoles.AsNoTracking().AnyAsync(x => x.UserId == userId && platformRoleIds.Contains(x.RoleId), cancellationToken))
                throw new AssociationIsolationException("A platform account cannot become a tenant account.");

            var binding = AssociationAccountBindings.Local.SingleOrDefault(x => x.ApplicationUserId == userId)
                ?? await AssociationAccountBindings.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationUserId == userId, cancellationToken);
            if ((binding is not null && binding.AssociationId != associationId)
                || await AssociationUserMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.ApplicationUserId == userId && x.AssociationId != associationId, cancellationToken)
                || await Members.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.ApplicationUserId == userId && x.AssociationId != associationId, cancellationToken))
                throw new AssociationIsolationException("The account already belongs to another association, including inactive memberships.");
            if (binding is null)
                AssociationAccountBindings.Add(new AssociationAccountBinding { ApplicationUserId = userId, AssociationId = associationId });
        }
        foreach (var userId in pendingPlatformUsers)
        {
            if (AssociationAccountBindings.Local.Any(x => x.ApplicationUserId == userId)
                || await AssociationAccountBindings.AsNoTracking().AnyAsync(x => x.ApplicationUserId == userId, cancellationToken))
                throw new AssociationIsolationException("A tenant account cannot become a platform account.");
        }
    }

    private void EnforceAssociationOwnership()
    {
        foreach (var entry in ChangeTracker.Entries<IAssociationOwned>().ToArray())
        {
            if (!IsAssociationResolved)
            {
                throw new AssociationIsolationException("Resolve an association before saving association-owned data.");
            }
            if (entry.Entity.Association is { } association && association.Id != CurrentAssociationId)
            {
                throw new AssociationIsolationException("The association navigation does not match the current association.");
            }
            if (entry.State == EntityState.Added && entry.Entity.AssociationId == 0)
            {
                entry.Property(x => x.AssociationId).CurrentValue = CurrentAssociationId;
            }
            if (entry.Entity.AssociationId != CurrentAssociationId
                || (entry.State != EntityState.Added && entry.Property(x => x.AssociationId).OriginalValue != CurrentAssociationId))
            {
                throw new AssociationIsolationException("Association ownership cannot be changed or supplied for another association.");
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        foreach (var entity in builder.Model.GetEntityTypes().Where(x => typeof(IAssociationOwned).IsAssignableFrom(x.ClrType)))
        {
            var parameter = Expression.Parameter(entity.ClrType, "entity");
            var predicate = Expression.AndAlso(
                Expression.Property(Expression.Constant(this), nameof(IsAssociationResolved)),
                Expression.Equal(Expression.Property(parameter, nameof(IAssociationOwned.AssociationId)),
                    Expression.Property(Expression.Constant(this), nameof(CurrentAssociationId))));
            builder.Entity(entity.ClrType).HasQueryFilter(Expression.Lambda(predicate, parameter));
        }

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.FirstName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.MiddleName)
                .HasMaxLength(100);

            entity.Property(x => x.DisplayName)
                .HasMaxLength(200);

            entity.Property(x => x.MustChangePassword)
                .HasDefaultValue(false);

            entity.Property(x => x.CreatedAt)
                .IsRequired();
        });

        builder.Entity<IdentityRole>().HasData(
            SeedDataConstants.Roles.Select(x => new IdentityRole
            {
                Id = x.Id,
                Name = x.Name,
                NormalizedName = x.Name.ToUpperInvariant(),
                ConcurrencyStamp = x.Id
            }).ToArray());
    }
}
