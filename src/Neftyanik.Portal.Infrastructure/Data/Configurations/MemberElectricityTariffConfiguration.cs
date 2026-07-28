using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class MemberElectricityTariffConfiguration : IEntityTypeConfiguration<MemberElectricityTariff>
{
    public void Configure(EntityTypeBuilder<MemberElectricityTariff> builder)
    {
        builder.ToTable("MemberElectricityTariffs", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_MemberElectricityTariffs_Rate_NonNegative", "[Rate] >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EffectiveFrom)
            .HasColumnType("date");

        builder.Property(x => x.Rate)
            .HasPrecision(18, 4);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.CreatedByUserId)
            .HasMaxLength(450);

        builder.HasIndex(x => x.EffectiveFrom)
            .IsUnique();

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.CreatedMemberElectricityTariffs)
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
