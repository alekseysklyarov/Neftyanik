using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ChargeTypes;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
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

    public IReadOnlyList<ChargeTypeListItemViewModel> ChargeTypes { get; private set; } = [];

    public int TotalPages { get; private set; }

    public string EmptyStateMessage { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
        Status = NormalizeStatus(Status);
        PageNumber = PageNumber < 1 ? 1 : PageNumber;

        var query = _dbContext.ChargeTypes.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(chargeType => chargeType.Name.Contains(Search));
        }

        query = Status switch
        {
            "archived" => query.Where(chargeType => !chargeType.IsActive),
            "all" => query,
            _ => query.Where(chargeType => chargeType.IsActive)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        TotalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)PageSize);
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        ChargeTypes = await query
            .OrderBy(chargeType => chargeType.Name)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(chargeType => new ChargeTypeListItemViewModel
            {
                Id = chargeType.Id,
                Name = chargeType.Name,
                DefaultAmount = chargeType.DefaultAmount,
                IsDefault = chargeType.IsDefault,
                IsYearly = chargeType.IsYearly,
                OnlyOnOwnerChange = chargeType.OnlyOnOwnerChange,
                IsActive = chargeType.IsActive,
                ChargesCount = chargeType.Charges.Count()
            })
            .ToListAsync(cancellationToken);

        EmptyStateMessage = totalCount == 0 && string.IsNullOrWhiteSpace(Search) && Status == "all"
            ? "Типы начислений пока не добавлены."
            : "По выбранным условиям типы начислений не найдены.";
    }

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    private static string NormalizeStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "all" => "all",
            "archived" => "archived",
            _ => "active"
        };
    }

    public sealed class ChargeTypeListItemViewModel
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public decimal? DefaultAmount { get; init; }

        public bool IsDefault { get; init; }

        public bool IsYearly { get; init; }

        public bool OnlyOnOwnerChange { get; init; }

        public bool IsActive { get; init; }

        public int ChargesCount { get; init; }
    }
}
