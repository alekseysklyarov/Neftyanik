using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class MemberElectricityMeterConfiguration : IEntityTypeConfiguration<MemberElectricityMeter>
{
    public void Configure(EntityTypeBuilder<MemberElectricityMeter> builder)
    {
        builder.ToTable("MemberElectricityMeters");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.MeterNumber)
            .HasMaxLength(100);

        builder.Property(x => x.Name)
            .HasMaxLength(200);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.CreatedByUserId)
            .HasMaxLength(450);

        builder.HasIndex(x => x.MemberId);

        builder.HasIndex(x => x.BillingPlotId);

        builder.HasOne(x => x.Member)
            .WithMany(x => x.MemberElectricityMeters)
            .HasForeignKey(x => x.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.BillingPlot)
            .WithMany(x => x.BillingMemberElectricityMeters)
            .HasForeignKey(x => x.BillingPlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Plots)
            .WithOne(x => x.MemberElectricityMeter)
            .HasForeignKey(x => x.MemberElectricityMeterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.CreatedMemberElectricityMeters)
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
