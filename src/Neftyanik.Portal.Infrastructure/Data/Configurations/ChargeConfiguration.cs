using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class ChargeConfiguration : IEntityTypeConfiguration<Charge>
{
    public void Configure(EntityTypeBuilder<Charge> builder)
    {
        builder.ToTable("Charges", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_Charges_Amount_Positive", "[Amount] > 0");
            tableBuilder.HasCheckConstraint("CK_Charges_PeriodMonth_Range", "[PeriodMonth] IS NULL OR ([PeriodMonth] >= 1 AND [PeriodMonth] <= 12)");
            tableBuilder.HasCheckConstraint("CK_Charges_PeriodYear_Range", "[PeriodYear] IS NULL OR ([PeriodYear] >= 2000 AND [PeriodYear] <= 2100)");
            tableBuilder.HasCheckConstraint("CK_Charges_DueDate_NotEarlierThanChargeDate", "[DueDate] IS NULL OR [DueDate] >= [ChargeDate]");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(x => x.Description)
            .HasMaxLength(1000)
            .IsUnicode();

        builder.Property(x => x.CreatedByUserId)
            .HasMaxLength(450);

        builder.Property(x => x.CancellationReason)
            .HasMaxLength(500)
            .IsUnicode();

        builder.Property(x => x.ChargeDate)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(x => x.DueDate)
            .HasColumnType("date");

        builder.Property(x => x.PeriodYear);

        builder.Property(x => x.PeriodMonth);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.PlotId);

        builder.HasIndex(x => x.ChargeTypeId);

        builder.HasIndex(x => x.ChargeDate);

        builder.HasIndex(x => x.DueDate);

        builder.HasIndex(x => x.CancelledAtUtc);

        builder.HasOne(x => x.Plot)
            .WithMany(x => x.Charges)
            .HasForeignKey(x => x.PlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ChargeType)
            .WithMany(x => x.Charges)
            .HasForeignKey(x => x.ChargeTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.CreatedCharges)
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
