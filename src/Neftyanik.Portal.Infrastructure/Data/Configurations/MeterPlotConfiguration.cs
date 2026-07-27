using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class MeterPlotConfiguration : IEntityTypeConfiguration<MeterPlot>
{
    public void Configure(EntityTypeBuilder<MeterPlot> builder)
    {
        builder.ToTable("MeterPlots");

        builder.HasKey(x => new { x.MeterId, x.PlotId, x.ValidFrom });

        builder.HasOne(x => x.Meter)
            .WithMany(x => x.MeterPlots)
            .HasForeignKey(x => x.MeterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Plot)
            .WithMany(x => x.MeterPlots)
            .HasForeignKey(x => x.PlotId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}