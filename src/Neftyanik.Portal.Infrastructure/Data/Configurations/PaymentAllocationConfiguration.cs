using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("PaymentAllocations");

        builder.HasKey(x => x.Id);

        builder.ConfigureAssociationOwnership();

        builder.Property(x => x.Amount)
            .HasPrecision(18, 2);

        builder.HasIndex(x => new { x.AssociationId, x.PaymentId });

        builder.HasIndex(x => new { x.AssociationId, x.ChargeId });

        builder.HasOne(x => x.Payment)
            .WithMany(x => x.PaymentAllocations)
            .HasForeignKey(x => new { x.AssociationId, x.PaymentId })
            .HasPrincipalKey(x => new { x.AssociationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Charge)
            .WithMany(x => x.PaymentAllocations)
            .HasForeignKey(x => new { x.AssociationId, x.ChargeId })
            .HasPrincipalKey(x => new { x.AssociationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
