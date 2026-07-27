using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class ChargeConfiguration : IEntityTypeConfiguration<Charge>
{
    public void Configure(EntityTypeBuilder<Charge> builder)
    {
        builder.ToTable("Charges");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(x => x.CreatedByUserId)
            .HasMaxLength(450);

        builder.Property(x => x.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.Quantity)
            .HasPrecision(18, 3);

        builder.Property(x => x.UnitPrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.Amount)
            .HasPrecision(18, 2);

        builder.HasIndex(x => new { x.UserId, x.Status });

        builder.HasIndex(x => new { x.PlotId, x.PeriodYear, x.ChargeType });

        builder.HasOne(x => x.User)
            .WithMany(x => x.Charges)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Plot)
            .WithMany(x => x.Charges)
            .HasForeignKey(x => x.PlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Meter)
            .WithMany(x => x.Charges)
            .HasForeignKey(x => x.MeterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SourceReading)
            .WithMany()
            .HasForeignKey(x => x.SourceReadingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.CreatedCharges)
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}