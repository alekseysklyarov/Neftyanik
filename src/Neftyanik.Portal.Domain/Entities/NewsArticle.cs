namespace Neftyanik.Portal.Domain.Entities;

public class NewsArticle
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public string Content { get; set; } = string.Empty;

    public string? ImagePath { get; set; }

    public bool IsPublished { get; set; }

    public bool IsPinned { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;

    public ApplicationUser? CreatedByUser { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

}