using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class MeterReadingConfiguration : IEntityTypeConfiguration<MeterReading>
{
    public void Configure(EntityTypeBuilder<MeterReading> builder)
    {
        builder.ToTable("MeterReadings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TotalValue)
            .HasPrecision(18, 3);

        builder.Property(x => x.DayValue)
            .HasPrecision(18, 3);

        builder.Property(x => x.NightValue)
            .HasPrecision(18, 3);

        builder.Property(x => x.SubmittedByUserId)
            .HasMaxLength(450);

        builder.Property(x => x.ApprovedByUserId)
            .HasMaxLength(450);

        builder.Property(x => x.Comment)
            .HasMaxLength(1000);

        builder.Property(x => x.MeterPhotoPath)
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.MeterId, x.ReadingDate })
            .IsUnique();

        builder.HasOne(x => x.Meter)
            .WithMany(x => x.MeterReadings)
            .HasForeignKey(x => x.MeterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SubmittedByUser)
            .WithMany(x => x.SubmittedMeterReadings)
            .HasForeignKey(x => x.SubmittedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ApprovedByUser)
            .WithMany(x => x.ApprovedMeterReadings)
            .HasForeignKey(x => x.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}