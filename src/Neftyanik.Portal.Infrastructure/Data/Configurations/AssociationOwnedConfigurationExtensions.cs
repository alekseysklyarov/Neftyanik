using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

internal static class AssociationOwnedConfigurationExtensions
{
    public static void ConfigureAssociationOwnership<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IAssociationOwned
    {
        // Explicit mapping breaks EF's type-mapping cycle through Plot and its billing meter.
        builder.Property(x => x.AssociationId)
            .HasConversion(value => value, value => value);

        builder.HasOne(x => x.Association)
            .WithMany()
            .HasForeignKey(x => x.AssociationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
