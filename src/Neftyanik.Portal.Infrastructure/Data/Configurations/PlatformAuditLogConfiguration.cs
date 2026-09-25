using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public sealed class PlatformAuditLogConfiguration : IEntityTypeConfiguration<PlatformAuditLog>
{
    public void Configure(EntityTypeBuilder<PlatformAuditLog> builder)
    {
        builder.ToTable("PlatformAuditLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OperatorUserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.OccurredAtUtc).IsRequired();
        builder.Property(x => x.Action).IsRequired().HasMaxLength(64);
        builder.Property(x => x.OldValuesJson).IsRequired().HasMaxLength(10000);
        builder.Property(x => x.NewValuesJson).IsRequired().HasMaxLength(10000);
        builder.HasIndex(x => new { x.AssociationId, x.OccurredAtUtc });
        builder.HasOne<Association>().WithMany().HasForeignKey(x => x.AssociationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OperatorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
