using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.FinancialAuditLog;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class IndexModel : PageModel
{
    private const int PageSize = 50;
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public DateOnly? DateFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? DateTo { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? User { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EntityType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Action { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<AuditLogListItemViewModel> Entries { get; private set; } = [];

    public int TotalPages { get; private set; }

    public int TotalCount { get; private set; }

    public string EmptyStateMessage { get; private set; } = string.Empty;

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        User = Normalize(User);
        EntityType = Normalize(EntityType);
        Action = Normalize(Action);
        Search = Normalize(Search);
        PageNumber = PageNumber < 1 ? 1 : PageNumber;

        var query = _dbContext.FinancialAuditLogs
            .AsNoTracking()
            .AsQueryable();

        if (DateFrom.HasValue)
        {
            var fromUtc = CreateUtcStart(DateFrom.Value);
            query = query.Where(item => item.CreatedAtUtc >= fromUtc);
        }

        if (DateTo.HasValue)
        {
            var toExclusiveUtc = CreateUtcStart(DateTo.Value.AddDays(1));
            query = query.Where(item => item.CreatedAtUtc < toExclusiveUtc);
        }

        if (!string.IsNullOrWhiteSpace(User))
        {
            query = query.Where(item =>
                (item.UserName != null && item.UserName.Contains(User))
                || (item.UserId != null && item.UserId.Contains(User)));
        }

        if (!string.IsNullOrWhiteSpace(EntityType))
        {
            query = query.Where(item => item.EntityType.Contains(EntityType));
        }

        if (!string.IsNullOrWhiteSpace(Action))
        {
            query = query.Where(item => item.Action.Contains(Action));
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(item => item.EntityId.Contains(Search)
                || (item.Description != null && item.Description.Contains(Search))
                || (item.UserName != null && item.UserName.Contains(Search))
                || item.EntityType.Contains(Search)
                || item.Action.Contains(Search));
        }

        TotalCount = await query.CountAsync(cancellationToken);
        TotalPages = TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        Entries = await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(item => new AuditLogListItemViewModel
            {
                Id = item.Id,
                CreatedAtUtc = item.CreatedAtUtc,
                UserName = item.UserName,
                UserId = item.UserId,
                Action = item.Action,
                EntityType = item.EntityType,
                EntityId = item.EntityId,
                Description = item.Description
            })
            .ToListAsync(cancellationToken);

        EmptyStateMessage = HasFilters()
            ? AppLocalizer.Get(
                "По выбранным условиям записи финансового аудита не найдены.",
                "За вибраними умовами записи фінансового аудиту не знайдено.",
                "No financial audit entries were found for the selected criteria.")
            : AppLocalizer.Get(
                "Записи финансового аудита пока отсутствуют.",
                "Записи фінансового аудиту ще відсутні.",
                "There are no financial audit entries yet.");
    }

    public sealed class AuditLogListItemViewModel
    {
        public long Id { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        public string? UserName { get; init; }

        public string? UserId { get; init; }

        public string Action { get; init; } = string.Empty;

        public string EntityType { get; init; } = string.Empty;

        public string EntityId { get; init; } = string.Empty;

        public string? Description { get; init; }

        public string UserDisplayName => !string.IsNullOrWhiteSpace(UserName)
            ? UserName
            : !string.IsNullOrWhiteSpace(UserId)
                ? UserId
                : "—";
    }

    private bool HasFilters()
    {
        return DateFrom.HasValue
            || DateTo.HasValue
            || !string.IsNullOrWhiteSpace(User)
            || !string.IsNullOrWhiteSpace(EntityType)
            || !string.IsNullOrWhiteSpace(Action)
            || !string.IsNullOrWhiteSpace(Search);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static DateTime CreateUtcStart(DateOnly value)
    {
        return DateTime.SpecifyKind(value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
    }
}
