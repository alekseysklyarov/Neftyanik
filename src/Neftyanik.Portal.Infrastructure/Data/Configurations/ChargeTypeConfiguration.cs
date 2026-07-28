using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class ChargeTypeConfiguration : IEntityTypeConfiguration<ChargeType>
{
    public void Configure(EntityTypeBuilder<ChargeType> builder)
    {
        builder.ToTable("ChargeTypes", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_ChargeTypes_DefaultAmount_Positive", "[DefaultAmount] IS NULL OR [DefaultAmount] > 0");
            tableBuilder.HasCheckConstraint("CK_ChargeTypes_YearlyAndOwnerChangeExclusive", "[IsYearly] = 0 OR [OnlyOnOwnerChange] = 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .HasMaxLength(64)
            .IsUnicode(false);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150)
            .IsUnicode();

        builder.Property(x => x.Description)
            .HasMaxLength(1000)
            .IsUnicode();

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        builder.Property(x => x.IsDefault)
            .HasDefaultValue(false);

        builder.Property(x => x.IsYearly)
            .HasDefaultValue(false);

        builder.Property(x => x.OnlyOnOwnerChange)
            .HasDefaultValue(false);

        builder.Property(x => x.DefaultAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.Name);

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasFilter("[Code] IS NOT NULL");

        builder.HasIndex(x => x.IsDefault)
            .IsUnique()
            .HasFilter("[IsDefault] = 1 AND [IsActive] = 1");
    }
}
