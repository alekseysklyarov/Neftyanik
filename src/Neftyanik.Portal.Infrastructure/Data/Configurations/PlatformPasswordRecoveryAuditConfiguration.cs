using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public sealed class PlatformPasswordRecoveryAuditConfiguration : IEntityTypeConfiguration<PlatformPasswordRecoveryAudit>
{
    public void Configure(EntityTypeBuilder<PlatformPasswordRecoveryAudit> builder)
    {
        builder.ToTable("PlatformPasswordRecoveryAudits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.OperatorIdentity).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ApprovalReference).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.OccurredAtUtc });
    }
}
