using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class PlotConfiguration : IEntityTypeConfiguration<Plot>
{
    public void Configure(EntityTypeBuilder<Plot> builder)
    {
        builder.ToTable("Plots");

        builder.HasKey(x => x.Id);

        builder.ConfigureAssociationOwnership();

        builder.Property(x => x.Number)
            .IsRequired()
            .HasMaxLength(50)
            .IsUnicode();

        builder.Property(x => x.Address)
            .HasMaxLength(250)
            .IsUnicode();

        builder.Property(x => x.AreaSquareMeters)
            .HasPrecision(18, 2);

        builder.Property(x => x.CadastralNumber)
            .HasMaxLength(100)
            .IsUnicode();

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        builder.Property(x => x.Notes)
            .HasMaxLength(2000)
            .IsUnicode();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.AssociationId, x.Number })
            .IsUnique();

        builder.HasIndex(x => new { x.AssociationId, x.CadastralNumber });

        builder.HasIndex(x => new { x.AssociationId, x.MemberElectricityMeterId });

        builder.HasOne(x => x.MemberElectricityMeter)
            .WithMany(x => x.Plots)
            .HasForeignKey(x => new { x.AssociationId, x.MemberElectricityMeterId })
            .HasPrincipalKey(x => new { x.AssociationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
