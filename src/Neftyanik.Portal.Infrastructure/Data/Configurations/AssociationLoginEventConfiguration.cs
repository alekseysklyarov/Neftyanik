using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public sealed class AssociationLoginEventConfiguration : IEntityTypeConfiguration<AssociationLoginEvent>
{
    public void Configure(EntityTypeBuilder<AssociationLoginEvent> builder)
    {
        builder.ToTable("AssociationLoginEvents");
        builder.HasKey(x => x.Id);
        builder.ConfigureAssociationOwnership();
        builder.HasIndex(x => x.UserLoginHistoryId).IsUnique();
        builder.HasOne(x => x.UserLoginHistory).WithMany()
            .HasForeignKey(x => x.UserLoginHistoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
