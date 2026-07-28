using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class AssociationElectricityReadingConfiguration : IEntityTypeConfiguration<AssociationElectricityReading>
{
    public void Configure(EntityTypeBuilder<AssociationElectricityReading> builder)
    {
        builder.ToTable("AssociationElectricityReadings", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_AssociationElectricityReadings_CurrentDayReading_NonNegative", "[CurrentDayReading] >= 0");
            tableBuilder.HasCheckConstraint("CK_AssociationElectricityReadings_CurrentNightReading_NonNegative", "[CurrentNightReading] >= 0");
            tableBuilder.HasCheckConstraint("CK_AssociationElectricityReadings_PreviousDayReading_NonNegative", "[PreviousDayReading] IS NULL OR [PreviousDayReading] >= 0");
            tableBuilder.HasCheckConstraint("CK_AssociationElectricityReadings_PreviousNightReading_NonNegative", "[PreviousNightReading] IS NULL OR [PreviousNightReading] >= 0");
            tableBuilder.HasCheckConstraint("CK_AssociationElectricityReadings_DayConsumption_NonNegative", "[DayConsumption] IS NULL OR [DayConsumption] >= 0");
            tableBuilder.HasCheckConstraint("CK_AssociationElectricityReadings_NightConsumption_NonNegative", "[NightConsumption] IS NULL OR [NightConsumption] >= 0");
            tableBuilder.HasCheckConstraint("CK_AssociationElectricityReadings_TotalConsumption_NonNegative", "[TotalConsumption] IS NULL OR [TotalConsumption] >= 0");
            tableBuilder.HasCheckConstraint("CK_AssociationElectricityReadings_DayAmount_NonNegative", "[DayAmount] IS NULL OR [DayAmount] >= 0");
            tableBuilder.HasCheckConstraint("CK_AssociationElectricityReadings_NightAmount_NonNegative", "[NightAmount] IS NULL OR [NightAmount] >= 0");
            tableBuilder.HasCheckConstraint("CK_AssociationElectricityReadings_TotalSupplierAmount_NonNegative", "[TotalSupplierAmount] IS NULL OR [TotalSupplierAmount] >= 0");
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

        builder.Property(x => x.AppliedSupplierDayRate)
            .HasPrecision(18, 4);

        builder.Property(x => x.DayAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.PreviousNightReading)
            .HasPrecision(18, 3);

        builder.Property(x => x.CurrentNightReading)
            .HasPrecision(18, 3);

        builder.Property(x => x.NightConsumption)
            .HasPrecision(18, 3);

        builder.Property(x => x.AppliedSupplierNightRate)
            .HasPrecision(18, 4);

        builder.Property(x => x.NightAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.TotalConsumption)
            .HasPrecision(18, 3);

        builder.Property(x => x.TotalSupplierAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.CreatedByUserId)
            .HasMaxLength(450);

        builder.HasIndex(x => x.ReadingDate)
            .IsUnique();

        builder.HasIndex(x => x.IsInitialReading)
            .IsUnique()
            .HasFilter("[IsInitialReading] = 1");

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.CreatedAssociationElectricityReadings)
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
