using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class ElectricityTariffConfiguration : IEntityTypeConfiguration<ElectricityTariff>
{
    public void Configure(EntityTypeBuilder<ElectricityTariff> builder)
    {
        builder.ToTable("ElectricityTariffs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.SingleRatePrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.DayRatePrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.NightRatePrice)
            .HasPrecision(18, 2);

        builder.HasIndex(x => x.EffectiveFrom);

        builder.Ignore(x => x.Rate);

        builder.HasData(new ElectricityTariff
        {
            Id = SeedDataConstants.InitialElectricityTariffId,
            Name = "??????? ?????",
            SingleRatePrice = 5.00m,
            EffectiveFrom = SeedDataConstants.InitialTariffEffectiveFrom,
            IsActive = true,
            CreatedAt = SeedDataConstants.SeedCreatedAt
        });
    }
}