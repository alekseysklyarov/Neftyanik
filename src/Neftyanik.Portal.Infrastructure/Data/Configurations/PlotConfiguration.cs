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

        builder.Property(x => x.Number)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.AreaSquareMeters)
            .HasPrecision(18, 2);

        builder.Property(x => x.Address)
            .HasMaxLength(500);

        builder.Property(x => x.OwnerId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.Number)
            .IsUnique();

        builder.HasOne(x => x.Owner)
            .WithMany(x => x.Plots)
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(x => x.Area);
        builder.Ignore(x => x.Status);
    }
}