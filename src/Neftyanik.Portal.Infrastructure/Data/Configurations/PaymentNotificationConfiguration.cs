using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class PaymentNotificationConfiguration : IEntityTypeConfiguration<PaymentNotification>
{
    public void Configure(EntityTypeBuilder<PaymentNotification> builder)
    {
        builder.ToTable("PaymentNotifications", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_PaymentNotifications_Amount_Positive", "[Amount] > 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount)
            .HasPrecision(18, 2);

        builder.Property(x => x.PaymentMethod)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(PaymentNotification.DescriptionMaxLength)
            .IsUnicode();

        builder.Property(x => x.AdministratorComment)
            .HasMaxLength(PaymentNotification.AdministratorCommentMaxLength)
            .IsUnicode();

        builder.Property(x => x.ReviewedByUserId)
            .HasMaxLength(450);

        builder.Property(x => x.Status);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.ReviewVersion)
            .HasDefaultValue(0)
            .IsConcurrencyToken();

        builder.HasIndex(x => x.MemberId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => x.PaymentId)
            .IsUnique();

        builder.HasOne(x => x.Member)
            .WithMany(x => x.PaymentNotifications)
            .HasForeignKey(x => x.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReviewedByUser)
            .WithMany(x => x.ReviewedPaymentNotifications)
            .HasForeignKey(x => x.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Payment)
            .WithOne(x => x.PaymentNotification)
            .HasForeignKey<PaymentNotification>(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
