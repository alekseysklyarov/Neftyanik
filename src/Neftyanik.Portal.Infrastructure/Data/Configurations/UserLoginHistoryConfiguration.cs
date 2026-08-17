using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class UserLoginHistoryConfiguration : IEntityTypeConfiguration<UserLoginHistory>
{
    public void Configure(EntityTypeBuilder<UserLoginHistory> builder)
    {
        builder.ToTable("UserLoginHistories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(x => x.LoggedInAtUtc)
            .IsRequired();

        builder.Property(x => x.IpAddress)
            .HasMaxLength(UserLoginHistory.IpAddressMaxLength);

        builder.Property(x => x.UserAgent)
            .HasMaxLength(UserLoginHistory.UserAgentMaxLength)
            .IsUnicode();

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.LoggedInAtUtc);
        builder.HasIndex(x => new { x.UserId, x.LoggedInAtUtc });

        builder.HasOne(x => x.User)
            .WithMany(x => x.UserLoginHistories)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
