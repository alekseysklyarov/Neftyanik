using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class ElectricityReadingConfiguration : IEntityTypeConfiguration<ElectricityReading>
{
    public void Configure(EntityTypeBuilder<ElectricityReading> builder)
    {
        builder.ToTable("ElectricityReadings", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_ElectricityReadings_CurrentDayReading_NonNegative", "[CurrentDayReading] >= 0");
            tableBuilder.HasCheckConstraint("CK_ElectricityReadings_CurrentNightReading_NonNegative", "[CurrentNightReading] >= 0");
            tableBuilder.HasCheckConstraint("CK_ElectricityReadings_PreviousDayReading_NonNegative", "[PreviousDayReading] IS NULL OR [PreviousDayReading] >= 0");
            tableBuilder.HasCheckConstraint("CK_ElectricityReadings_PreviousNightReading_NonNegative", "[PreviousNightReading] IS NULL OR [PreviousNightReading] >= 0");
            tableBuilder.HasCheckConstraint("CK_ElectricityReadings_DayConsumption_NonNegative", "[DayConsumption] IS NULL OR [DayConsumption] >= 0");
            tableBuilder.HasCheckConstraint("CK_ElectricityReadings_NightConsumption_NonNegative", "[NightConsumption] IS NULL OR [NightConsumption] >= 0");
            tableBuilder.HasCheckConstraint("CK_ElectricityReadings_DayAmount_NonNegative", "[DayAmount] IS NULL OR [DayAmount] >= 0");
            tableBuilder.HasCheckConstraint("CK_ElectricityReadings_NightAmount_NonNegative", "[NightAmount] IS NULL OR [NightAmount] >= 0");
            tableBuilder.HasCheckConstraint("CK_ElectricityReadings_TotalAmount_NonNegative", "[TotalAmount] IS NULL OR [TotalAmount] >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ReadingDate)
            .HasColumnType("date");

        builder.Property(x => x.PreviousDayReading)
            .HasPrecision(18, 3);

        builder.Property(x => x.CurrentDayReading)
            .HasPrecision(18, 3);

        builder.Property(x => x.DayConsumption)
            .HasPrecision(18, 3);

        builder.Property(x => x.DayRate)
            .HasPrecision(18, 4);

        builder.Property(x => x.DayAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.PreviousNightReading)
            .HasPrecision(18, 3);

        builder.Property(x => x.CurrentNightReading)
            .HasPrecision(18, 3);

        builder.Property(x => x.NightConsumption)
            .HasPrecision(18, 3);

        builder.Property(x => x.NightRate)
            .HasPrecision(18, 4);

        builder.Property(x => x.NightAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.TotalAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.CreatedByUserId)
            .HasMaxLength(450);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.PlotId);

        builder.HasIndex(x => new { x.PlotId, x.ReadingDate })
            .IsUnique();

        builder.HasIndex(x => x.ChargeId)
            .IsUnique()
            .HasFilter("[ChargeId] IS NOT NULL");

        builder.HasOne(x => x.Plot)
            .WithMany(x => x.ElectricityReadings)
            .HasForeignKey(x => x.PlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Charge)
            .WithOne(x => x.ElectricityReading)
            .HasForeignKey<ElectricityReading>(x => x.ChargeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.CreatedElectricityReadings)
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
