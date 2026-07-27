using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

[Authorize(Roles = RoleNames.Administrator)]
public class IndexModel : PageModel
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "active";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<MemberListItem> Members { get; private set; } = [];

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; }

    public string EmptyStateMessage { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var normalizedStatus = NormalizeStatus(Status);
        Status = normalizedStatus;
        PageNumber = PageNumber < 1 ? 1 : PageNumber;
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();

        var query = _dbContext.Members.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(member =>
                member.FullName.Contains(Search) ||
                (member.PhoneNumber != null && member.PhoneNumber.Contains(Search)) ||
                (member.Email != null && member.Email.Contains(Search)));
        }

        query = normalizedStatus switch
        {
            "archived" => query.Where(member => !member.IsActive),
            "all" => query,
            _ => query.Where(member => member.IsActive)
        };

        TotalCount = await query.CountAsync(cancellationToken);
        TotalPages = TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        Members = await query
            .OrderBy(member => member.FullName)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(member => new MemberListItem
            {
                Id = member.Id,
                FullName = member.FullName,
                PhoneNumber = member.PhoneNumber,
                Email = member.Email,
                JoinedAt = member.JoinedAt,
                IsActive = member.IsActive,
                ActiveOwnershipsCount = member.PlotOwnerships.Count(ownership => ownership.ValidTo == null),
                LinkedAccount = member.ApplicationUserId == null
                    ? null
                    : member.ApplicationUser != null
                        ? member.ApplicationUser.DisplayName ?? member.ApplicationUser.Email ?? member.ApplicationUser.UserName
                        : member.ApplicationUserId
            })
            .ToListAsync(cancellationToken);

        EmptyStateMessage = TotalCount == 0 && string.IsNullOrWhiteSpace(Search) && normalizedStatus == "all"
            ? "Члены товарищества пока не добавлены."
            : "По выбранным условиям члены товарищества не найдены.";
    }

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public sealed class MemberListItem
    {
        public int Id { get; init; }

        public string FullName { get; init; } = string.Empty;

        public string? PhoneNumber { get; init; }

        public string? Email { get; init; }

        public DateOnly? JoinedAt { get; init; }

        public bool IsActive { get; init; }

        public int ActiveOwnershipsCount { get; init; }

        public string? LinkedAccount { get; init; }
    }

    private static string NormalizeStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "all" => "all",
            "archived" => "archived",
            _ => "active"
        };
    }
}
