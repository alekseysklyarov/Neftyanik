using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data.Configurations;

namespace Neftyanik.Portal.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Association> Associations => Set<Association>();

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

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        // Assign before DetectChanges can propagate an unassigned new meter's key to an existing plot.
        var autoDetectChanges = ChangeTracker.AutoDetectChangesEnabled;
        ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var entry in ChangeTracker.Entries<Association>()
                         .Where(x => x.State != EntityState.Deleted))
            {
                entry.Entity.ValidateSlug();
            }

            // Stage 1 only: remove this fallback when callers supply tenant ownership in Stage 2.
            var unassigned = ChangeTracker.Entries<IAssociationOwned>()
                .Where(x => x.State == EntityState.Added && x.Entity.AssociationId == 0 && x.Entity.Association is null)
                .ToArray();

            if (unassigned.Length > 0)
            {
                var associationId = await Associations.AsNoTracking()
                    .Where(x => x.Slug == SeedDataConstants.InitialAssociationSlug && x.IsActive)
                    .Select(x => (int?)x.Id)
                    .SingleOrDefaultAsync(cancellationToken);

                if (associationId is null)
                {
                    throw new InvalidOperationException("Stage 1 compatibility requires the existing active 'neftyanik' association. Apply the association foundation migration before writing business data.");
                }

                foreach (var entry in unassigned)
                {
                    if (entry.Entity.AssociationId == 0)
                    {
                        entry.Property(x => x.AssociationId).CurrentValue = associationId.Value;
                    }
                }
            }
        }
        finally
        {
            ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
        }

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

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
