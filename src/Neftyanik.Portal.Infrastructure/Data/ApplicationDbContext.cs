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

    public DbSet<Association> Associations => Set<Association>();

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
        if (ChangeTracker.Entries<IAssociationOwned>().Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
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
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        finally
        {
            ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
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
