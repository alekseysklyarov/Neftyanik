using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_Payments_Amount_Positive", "[Amount] > 0");
        });

        builder.HasKey(x => x.Id);

        builder.ConfigureAssociationOwnership();

        builder.Property(x => x.CreatedByUserId)
            .HasMaxLength(450);

        builder.Property(x => x.Amount)
            .HasPrecision(18, 2);

        builder.Property(x => x.BalanceBeforePayment)
            .HasPrecision(18, 2);

        builder.Property(x => x.BalanceAfterPayment)
            .HasPrecision(18, 2);

        builder.Property(x => x.ReferenceNumber)
            .HasMaxLength(150)
            .IsUnicode();

        builder.Property(x => x.Description)
            .HasMaxLength(1000)
            .IsUnicode();

        builder.Property(x => x.CancellationReason)
            .HasMaxLength(500)
            .IsUnicode();

        builder.Property(x => x.PaymentDate)
            .HasColumnType("date");

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.AssociationId, x.MemberId });

        builder.HasIndex(x => new { x.AssociationId, x.PlotId });

        builder.HasIndex(x => new { x.AssociationId, x.PaymentDate });

        builder.HasIndex(x => new { x.AssociationId, x.CancelledAtUtc });

        builder.HasIndex(x => new { x.AssociationId, x.ReferenceNumber });

        builder.HasOne(x => x.Member)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => new { x.AssociationId, x.MemberId })
            .HasPrincipalKey(x => new { x.AssociationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Plot)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => new { x.AssociationId, x.PlotId })
            .HasPrincipalKey(x => new { x.AssociationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.CreatedPayments)
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
