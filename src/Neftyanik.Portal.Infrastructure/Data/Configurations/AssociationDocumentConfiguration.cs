using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Data.Configurations;

public class AssociationDocumentConfiguration : IEntityTypeConfiguration<AssociationDocument>
{
    public void Configure(EntityTypeBuilder<AssociationDocument> builder)
    {
        builder.ToTable("AssociationDocuments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasMaxLength(1000);

        builder.Property(x => x.FilePath)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.OriginalFileName)
            .IsRequired()
            .HasMaxLength(260);

        builder.Property(x => x.UploadedByUserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.HasOne(x => x.UploadedByUser)
            .WithMany(x => x.UploadedDocuments)
            .HasForeignKey(x => x.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}