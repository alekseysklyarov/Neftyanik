using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class MemberElectricityMeterPlotConfiguration : IEntityTypeConfiguration<MemberElectricityMeterPlot>
{
    public void Configure(EntityTypeBuilder<MemberElectricityMeterPlot> builder)
    {
        builder.ToTable("MemberElectricityMeterPlots");

        builder.HasKey(x => new { x.MemberElectricityMeterId, x.PlotId });

        builder.HasIndex(x => x.PlotId);

        builder.HasOne(x => x.MemberElectricityMeter)
            .WithMany(x => x.MeterPlots)
            .HasForeignKey(x => x.MemberElectricityMeterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Plot)
            .WithMany(x => x.MemberElectricityMeterPlots)
            .HasForeignKey(x => x.PlotId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
