using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class FinancialAuditLogConfiguration : IEntityTypeConfiguration<FinancialAuditLog>
{
    public void Configure(EntityTypeBuilder<FinancialAuditLog> builder)
    {
        builder.ToTable("FinancialAuditLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UserId)
            .HasMaxLength(FinancialAuditLog.UserIdMaxLength);

        builder.Property(x => x.UserName)
            .HasMaxLength(FinancialAuditLog.UserNameMaxLength);

        builder.Property(x => x.Action)
            .IsRequired()
            .HasMaxLength(FinancialAuditLog.ActionMaxLength);

        builder.Property(x => x.EntityType)
            .IsRequired()
            .HasMaxLength(FinancialAuditLog.EntityTypeMaxLength);

        builder.Property(x => x.EntityId)
            .IsRequired()
            .HasMaxLength(FinancialAuditLog.EntityIdMaxLength);

        builder.Property(x => x.Description)
            .HasMaxLength(FinancialAuditLog.DescriptionMaxLength)
            .IsUnicode();

        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
