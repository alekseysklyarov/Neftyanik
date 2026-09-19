using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public sealed class PlatformBootstrapStateConfiguration : IEntityTypeConfiguration<PlatformBootstrapState>
{
    public void Configure(EntityTypeBuilder<PlatformBootstrapState> builder)
    {
        builder.ToTable("PlatformBootstrapStates", table => table.HasCheckConstraint("CK_PlatformBootstrapStates_Singleton", "[Id] = 1"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Reason).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Disposition).HasConversion<int>();
        builder.Property(x => x.OperatorIdentity).HasMaxLength(256);
        builder.Property(x => x.ApprovalReference).HasMaxLength(100);
        builder.Property(x => x.InitializedUserId).HasMaxLength(450);
    }
}
