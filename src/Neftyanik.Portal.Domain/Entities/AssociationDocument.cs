using Neftyanik.Portal.Domain.Enums;

namespace Neftyanik.Portal.Domain.Entities;

public class AssociationDocument
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DocumentType DocumentType { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;

    public bool IsPublic { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public string UploadedByUserId { get; set; } = string.Empty;

    public ApplicationUser? UploadedByUser { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

}