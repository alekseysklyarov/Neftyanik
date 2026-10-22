using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public sealed class DemoRequestConfiguration : IEntityTypeConfiguration<DemoRequest>
{
    public void Configure(EntityTypeBuilder<DemoRequest> builder)
    {
        builder.ToTable("DemoRequests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ContactName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.AssociationName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(254).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(16);
        builder.Property(x => x.PreferredContactMethod).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(1000);
        builder.Property(x => x.ConsentVersion).HasMaxLength(40).IsRequired();
        builder.Property(x => x.PilotStage).HasMaxLength(24).IsRequired();
        builder.Property(x => x.PilotNotes).HasMaxLength(4000);
        builder.Property(x => x.NextAction).HasMaxLength(500);
        builder.Property(x => x.PilotRevision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.ContactedAtUtc, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.NotificationSentAtUtc, x.NotificationNextAttemptAtUtc });
    }
}
