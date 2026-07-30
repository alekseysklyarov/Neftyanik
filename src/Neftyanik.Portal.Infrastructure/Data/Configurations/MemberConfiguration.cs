using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("Members");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FullName)
            .IsRequired()
            .HasMaxLength(200)
            .IsUnicode();

        builder.Property(x => x.PhoneNumber)
            .HasMaxLength(50)
            .IsUnicode();

        builder.Property(x => x.Email)
            .HasMaxLength(256)
            .IsUnicode();

        builder.Property(x => x.ApplicationUserId)
            .HasMaxLength(450)
            .IsUnicode();

        builder.Property(x => x.ElectricityMeterType)
            .HasDefaultValue(MemberElectricityMeterType.SingleRate);

        builder.Property(x => x.IsElectricityDisconnected)
            .HasDefaultValue(false);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        builder.Property(x => x.Notes)
            .HasMaxLength(2000)
            .IsUnicode();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.FullName);

        builder.HasIndex(x => x.Email);

        builder.HasOne(x => x.ApplicationUser)
            .WithMany(x => x.Members)
            .HasForeignKey(x => x.ApplicationUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
