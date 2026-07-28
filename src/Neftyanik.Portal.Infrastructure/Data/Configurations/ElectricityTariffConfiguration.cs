using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class ElectricityTariffConfiguration : IEntityTypeConfiguration<ElectricityTariff>
{
    public void Configure(EntityTypeBuilder<ElectricityTariff> builder)
    {
        builder.ToTable("ElectricityTariffs", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_ElectricityTariffs_DayRate_NonNegative", "[DayRate] >= 0");
            tableBuilder.HasCheckConstraint("CK_ElectricityTariffs_NightRate_NonNegative", "[NightRate] >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EffectiveFrom)
            .HasColumnType("date");

        builder.Property(x => x.DayRate)
            .HasPrecision(18, 4);

        builder.Property(x => x.NightRate)
            .HasPrecision(18, 4);

        builder.Property(x => x.CreatedByUserId)
            .HasMaxLength(450);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.EffectiveFrom)
            .IsUnique();

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.CreatedElectricityTariffs)
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
