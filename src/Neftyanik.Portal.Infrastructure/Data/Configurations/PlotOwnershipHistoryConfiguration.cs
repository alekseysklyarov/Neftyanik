using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class PlotOwnershipHistoryConfiguration : IEntityTypeConfiguration<PlotOwnershipHistory>
{
    public void Configure(EntityTypeBuilder<PlotOwnershipHistory> builder)
    {
        builder.ToTable("PlotOwnershipHistories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OwnerId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(x => x.Comment)
            .HasMaxLength(1000);

        builder.HasOne(x => x.Plot)
            .WithMany(x => x.OwnershipHistory)
            .HasForeignKey(x => x.PlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Owner)
            .WithMany(x => x.PlotOwnershipHistoryRecords)
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}