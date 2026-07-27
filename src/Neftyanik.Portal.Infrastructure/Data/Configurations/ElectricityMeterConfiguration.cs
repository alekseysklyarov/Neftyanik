using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class ElectricityMeterConfiguration : IEntityTypeConfiguration<ElectricityMeter>
{
    public void Configure(EntityTypeBuilder<ElectricityMeter> builder)
    {
        builder.ToTable("ElectricityMeters");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SerialNumber)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.OwnerId)
            .HasMaxLength(450);

        builder.Property(x => x.InitialReading)
            .HasPrecision(18, 3);

        builder.Property(x => x.InitialDayReading)
            .HasPrecision(18, 3);

        builder.Property(x => x.InitialNightReading)
            .HasPrecision(18, 3);

        builder.HasIndex(x => x.SerialNumber)
            .IsUnique();

        builder.HasOne(x => x.Owner)
            .WithMany(x => x.ElectricityMeters)
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}