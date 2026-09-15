namespace Neftyanik.Portal.Domain.Entities;

public class Association
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public void ValidateSlug()
    {
        if (string.IsNullOrEmpty(Slug) || Slug.Length > 100
            || !System.Text.RegularExpressions.Regex.IsMatch(Slug, "\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z"))
        {
            throw new InvalidOperationException("Association slug must contain lowercase ASCII letters or digits separated by single hyphens, and be at most 100 characters long.");
        }
    }
}
