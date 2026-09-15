using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class PlotOwnershipConfiguration : IEntityTypeConfiguration<PlotOwnership>
{
    public void Configure(EntityTypeBuilder<PlotOwnership> builder)
    {
        builder.ToTable("PlotOwnerships", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_PlotOwnerships_OwnershipShare_Range",
                "[OwnershipShare] IS NULL OR ([OwnershipShare] > 0 AND [OwnershipShare] <= 100)");

            tableBuilder.HasCheckConstraint(
                "CK_PlotOwnerships_ValidTo_NotEarlierThanValidFrom",
                "[ValidFrom] IS NULL OR [ValidTo] IS NULL OR [ValidTo] >= [ValidFrom]");
        });

        builder.HasKey(x => x.Id);

        builder.ConfigureAssociationOwnership();

        builder.Property(x => x.OwnershipShare)
            .HasPrecision(5, 2);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.AssociationId, x.MemberId });

        builder.HasIndex(x => new { x.AssociationId, x.PlotId })
            .IsUnique()
            .HasFilter("[ValidTo] IS NULL");

        builder.HasOne(x => x.Plot)
            .WithMany(x => x.PlotOwnerships)
            .HasForeignKey(x => new { x.AssociationId, x.PlotId })
            .HasPrincipalKey(x => new { x.AssociationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Member)
            .WithMany(x => x.PlotOwnerships)
            .HasForeignKey(x => new { x.AssociationId, x.MemberId })
            .HasPrincipalKey(x => new { x.AssociationId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
