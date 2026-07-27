using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class MembershipFeeRateConfiguration : IEntityTypeConfiguration<MembershipFeeRate>
{
    public void Configure(EntityTypeBuilder<MembershipFeeRate> builder)
    {
        builder.ToTable("MembershipFeeRates");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AmountPerPlot)
            .HasPrecision(18, 2);

        builder.HasIndex(x => x.Year)
            .IsUnique();

        builder.HasData(new MembershipFeeRate
        {
            Id = SeedDataConstants.InitialMembershipFeeRateId,
            Year = 2026,
            AmountPerPlot = 500.00m,
            DueDate = SeedDataConstants.InitialMembershipFeeDueDate,
            IsActive = true,
            CreatedAt = SeedDataConstants.SeedCreatedAt
        });
    }
}