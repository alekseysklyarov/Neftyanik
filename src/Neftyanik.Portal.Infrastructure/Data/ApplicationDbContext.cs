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

    public DbSet<Plot> Plots => Set<Plot>();

    public DbSet<Member> Members => Set<Member>();

    public DbSet<PlotOwnership> PlotOwnerships => Set<PlotOwnership>();

    public DbSet<PlotOwnershipHistory> PlotOwnershipHistories => Set<PlotOwnershipHistory>();

    public DbSet<ElectricityMeter> ElectricityMeters => Set<ElectricityMeter>();

    public DbSet<MeterPlot> MeterPlots => Set<MeterPlot>();

    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();

    public DbSet<ElectricityTariff> ElectricityTariffs => Set<ElectricityTariff>();

    public DbSet<MembershipFeeRate> MembershipFeeRates => Set<MembershipFeeRate>();

    public DbSet<Charge> Charges => Set<Charge>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();

    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();

    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<NewsArticle> NewsArticles => Set<NewsArticle>();

    public DbSet<AssociationDocument> AssociationDocuments => Set<AssociationDocument>();

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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
