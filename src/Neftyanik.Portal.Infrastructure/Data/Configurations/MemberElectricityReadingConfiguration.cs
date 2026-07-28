using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class MemberElectricityReadingConfiguration : IEntityTypeConfiguration<MemberElectricityReading>
{
    public void Configure(EntityTypeBuilder<MemberElectricityReading> builder)
    {
        builder.ToTable("MemberElectricityReadings", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_MemberElectricityReadings_CurrentReading_NonNegative", "[CurrentReading] >= 0");
            tableBuilder.HasCheckConstraint("CK_MemberElectricityReadings_PreviousReading_NonNegative", "[PreviousReading] IS NULL OR [PreviousReading] >= 0");
            tableBuilder.HasCheckConstraint("CK_MemberElectricityReadings_Consumption_NonNegative", "[Consumption] IS NULL OR [Consumption] >= 0");
            tableBuilder.HasCheckConstraint("CK_MemberElectricityReadings_AppliedMemberRate_NonNegative", "[AppliedMemberRate] IS NULL OR [AppliedMemberRate] >= 0");
            tableBuilder.HasCheckConstraint("CK_MemberElectricityReadings_Amount_NonNegative", "[Amount] IS NULL OR [Amount] >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ReadingDate)
            .HasColumnType("date");

        builder.Property(x => x.PreviousReading)
            .HasPrecision(18, 3);

        builder.Property(x => x.CurrentReading)
            .HasPrecision(18, 3);

        builder.Property(x => x.Consumption)
            .HasPrecision(18, 3);

        builder.Property(x => x.AppliedMemberRate)
            .HasPrecision(18, 4);

        builder.Property(x => x.Amount)
            .HasPrecision(18, 2);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.CreatedByUserId)
            .HasMaxLength(450);

        builder.HasIndex(x => new { x.MemberElectricityMeterId, x.ReadingDate })
            .IsUnique();

        builder.HasIndex(x => new { x.MemberElectricityMeterId, x.IsInitialReading })
            .IsUnique()
            .HasFilter("[IsInitialReading] = 1");

        builder.HasIndex(x => x.ChargeId)
            .IsUnique()
            .HasFilter("[ChargeId] IS NOT NULL");

        builder.HasOne(x => x.MemberElectricityMeter)
            .WithMany(x => x.Readings)
            .HasForeignKey(x => x.MemberElectricityMeterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Charge)
            .WithOne(x => x.MemberElectricityReading)
            .HasForeignKey<MemberElectricityReading>(x => x.ChargeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.CreatedMemberElectricityReadings)
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
