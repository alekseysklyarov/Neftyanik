using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .HasMaxLength(450);

        builder.Property(x => x.Action)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.EntityType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.EntityId)
            .HasMaxLength(100);

        builder.Property(x => x.IpAddress)
            .HasMaxLength(45);

        builder.HasIndex(x => new { x.EntityType, x.EntityId });

        builder.HasOne(x => x.User)
            .WithMany(x => x.AuditLogs)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}