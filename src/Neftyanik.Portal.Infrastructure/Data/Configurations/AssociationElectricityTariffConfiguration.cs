using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class AssociationElectricityTariffConfiguration : IEntityTypeConfiguration<AssociationElectricityTariff>
{
    public void Configure(EntityTypeBuilder<AssociationElectricityTariff> builder)
    {
        builder.ToTable("AssociationElectricityTariffs", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_AssociationElectricityTariffs_DayRate_NonNegative", "[DayRate] >= 0");
            tableBuilder.HasCheckConstraint("CK_AssociationElectricityTariffs_NightRate_NonNegative", "[NightRate] >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EffectiveFrom)
            .HasColumnType("date");

        builder.Property(x => x.DayRate)
            .HasPrecision(18, 4);

        builder.Property(x => x.NightRate)
            .HasPrecision(18, 4);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.CreatedByUserId)
            .HasMaxLength(450);

        builder.HasIndex(x => x.EffectiveFrom)
            .IsUnique();

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.CreatedAssociationElectricityTariffs)
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
