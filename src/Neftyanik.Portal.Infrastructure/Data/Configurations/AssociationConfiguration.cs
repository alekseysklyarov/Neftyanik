using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class AssociationConfiguration : IEntityTypeConfiguration<Association>
{
    public void Configure(EntityTypeBuilder<Association> builder)
    {
        builder.ToTable("Associations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(Neftyanik.Portal.Domain.Constants.AssociationMetadataLimits.Name);
        builder.Property(x => x.Slug).IsRequired().HasMaxLength(100).IsUnicode(false);
        builder.Property(x => x.ContactEmail).HasMaxLength(Neftyanik.Portal.Domain.Constants.AssociationMetadataLimits.ContactEmail);
        builder.Property(x => x.ContactPhone).HasMaxLength(Neftyanik.Portal.Domain.Constants.AssociationMetadataLimits.ContactPhone);
        builder.Property(x => x.PostalAddress).HasMaxLength(Neftyanik.Portal.Domain.Constants.AssociationMetadataLimits.PostalAddress);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasIndex(x => x.Slug).IsUnique();

        builder.HasData(new Association
        {
            Id = SeedDataConstants.InitialAssociationId,
            Name = "Нефтяник",
            Slug = SeedDataConstants.InitialAssociationSlug,
            IsActive = true,
            CreatedAtUtc = SeedDataConstants.SeedCreatedAt
        });
    }
}
