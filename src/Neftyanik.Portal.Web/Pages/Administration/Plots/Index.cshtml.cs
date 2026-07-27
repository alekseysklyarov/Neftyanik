using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots;

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

    public IReadOnlyList<PlotListItem> Plots { get; private set; } = [];

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; }

    public string EmptyStateMessage { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var normalizedStatus = NormalizeStatus(Status);
        Status = normalizedStatus;
        PageNumber = PageNumber < 1 ? 1 : PageNumber;
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();

        IQueryable<Plot> query = _dbContext.Plots.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(plot =>
                plot.Number.Contains(Search) ||
                (plot.Address != null && plot.Address.Contains(Search)) ||
                (plot.CadastralNumber != null && plot.CadastralNumber.Contains(Search)));
        }

        query = normalizedStatus switch
        {
            "archived" => query.Where(plot => !plot.IsActive),
            "all" => query,
            _ => query.Where(plot => plot.IsActive)
        };

        TotalCount = await query.CountAsync(cancellationToken);
        TotalPages = TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        Plots = await query
            .OrderBy(plot => plot.Number)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(plot => new PlotListItem
            {
                Id = plot.Id,
                Number = plot.Number,
                Address = plot.Address,
                AreaSquareMeters = plot.AreaSquareMeters,
                CadastralNumber = plot.CadastralNumber,
                IsActive = plot.IsActive,
                OwnersCount = plot.PlotOwnerships.Count(ownership => ownership.ValidTo == null),
                PrimaryContact = plot.PlotOwnerships
                    .Where(ownership => ownership.ValidTo == null && ownership.IsPrimaryContact && ownership.Member != null)
                    .Select(ownership => ownership.Member!.FullName)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        EmptyStateMessage = TotalCount == 0 && string.IsNullOrWhiteSpace(Search) && normalizedStatus == "all"
            ? "Участки пока не добавлены."
            : "По выбранным условиям участки не найдены.";
    }

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public sealed class PlotListItem
    {
        public int Id { get; init; }

        public string Number { get; init; } = string.Empty;

        public string? Address { get; init; }

        public decimal? AreaSquareMeters { get; init; }

        public string? CadastralNumber { get; init; }

        public bool IsActive { get; init; }

        public int OwnersCount { get; init; }

        public string? PrimaryContact { get; init; }
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
