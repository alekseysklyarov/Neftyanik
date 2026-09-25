using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public sealed class AssociationAccountBindingConfiguration : IEntityTypeConfiguration<AssociationAccountBinding>
{
    public void Configure(EntityTypeBuilder<AssociationAccountBinding> builder)
    {
        builder.ToTable("AssociationAccountBindings");
        builder.HasKey(x => x.ApplicationUserId);
        builder.Property(x => x.ApplicationUserId).HasMaxLength(450);
        builder.HasAlternateKey(x => new { x.ApplicationUserId, x.AssociationId });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApplicationUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Association>().WithMany().HasForeignKey(x => x.AssociationId).OnDelete(DeleteBehavior.Restrict);
    }
}
