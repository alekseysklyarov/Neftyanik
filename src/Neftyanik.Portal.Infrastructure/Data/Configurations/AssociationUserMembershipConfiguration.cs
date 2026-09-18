using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public sealed class AssociationUserMembershipConfiguration : IEntityTypeConfiguration<AssociationUserMembership>
{
    public void Configure(EntityTypeBuilder<AssociationUserMembership> builder)
    {
        builder.ToTable("AssociationUserMemberships", table => table.HasCheckConstraint(
            "CK_AssociationUserMemberships_Role", "[Role] IN ('Administrator', 'Accountant', 'Member')"));
        builder.HasKey(x => x.Id);
        builder.ConfigureAssociationOwnership();
        builder.Property(x => x.ApplicationUserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.Role).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasIndex(x => new { x.AssociationId, x.ApplicationUserId, x.Role }).IsUnique();
        builder.HasOne(x => x.ApplicationUser).WithMany()
            .HasForeignKey(x => x.ApplicationUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
