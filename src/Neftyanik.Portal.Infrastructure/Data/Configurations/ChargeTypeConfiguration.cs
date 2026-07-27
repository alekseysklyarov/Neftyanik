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
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150)
            .IsUnicode();

        builder.Property(x => x.Description)
            .HasMaxLength(1000)
            .IsUnicode();

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        builder.Property(x => x.DefaultAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.Name);
    }
}
