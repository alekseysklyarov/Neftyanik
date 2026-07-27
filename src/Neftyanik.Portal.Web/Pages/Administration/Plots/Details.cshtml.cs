using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots;

[Authorize(Roles = RoleNames.Administrator)]
public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public DetailsModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public PlotDetailsViewModel Plot { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var plot = await _dbContext.Plots
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new PlotDetailsViewModel
            {
                Id = item.Id,
                Number = item.Number,
                Address = item.Address,
                AreaSquareMeters = item.AreaSquareMeters,
                CadastralNumber = item.CadastralNumber,
                Notes = item.Notes,
                IsActive = item.IsActive,
                CreatedAtUtc = item.CreatedAtUtc,
                UpdatedAtUtc = item.UpdatedAtUtc,
                OwnersCount = item.PlotOwnerships.Count(ownership => ownership.ValidTo == null),
                HasPrimaryContact = item.PlotOwnerships.Any(ownership => ownership.ValidTo == null && ownership.IsPrimaryContact),
                PrimaryContact = item.PlotOwnerships
                    .Where(ownership => ownership.ValidTo == null && ownership.IsPrimaryContact && ownership.Member != null)
                    .Select(ownership => ownership.Member!.FullName)
                    .FirstOrDefault(),
                SpecifiedTotalShare = item.PlotOwnerships
                    .Where(ownership => ownership.ValidTo == null && ownership.OwnershipShare.HasValue)
                    .Sum(ownership => (decimal?)ownership.OwnershipShare) ?? 0m
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (plot is null)
        {
            return NotFound();
        }

        plot.CurrentOwnerships = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .Where(ownership => ownership.PlotId == id && ownership.ValidTo == null)
            .OrderByDescending(ownership => ownership.IsPrimaryContact)
            .ThenBy(ownership => ownership.Member != null ? ownership.Member.FullName : string.Empty)
            .Select(ownership => new PlotOwnershipViewModel
            {
                MemberId = ownership.MemberId,
                MemberFullName = ownership.Member != null ? ownership.Member.FullName : "—",
                OwnershipShare = ownership.OwnershipShare,
                IsPrimaryContact = ownership.IsPrimaryContact,
                ValidFrom = ownership.ValidFrom,
                ValidTo = ownership.ValidTo
            })
            .ToListAsync(cancellationToken);

        Plot = plot;
        return Page();
    }

    public sealed class PlotDetailsViewModel
    {
        public int Id { get; init; }

        public string Number { get; init; } = string.Empty;

        public string? Address { get; init; }

        public decimal? AreaSquareMeters { get; init; }

        public string? CadastralNumber { get; init; }

        public string? Notes { get; init; }

        public bool IsActive { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        public DateTime? UpdatedAtUtc { get; init; }

        public int OwnersCount { get; init; }

        public bool HasPrimaryContact { get; init; }

        public string? PrimaryContact { get; init; }

        public decimal SpecifiedTotalShare { get; init; }

        public IReadOnlyList<PlotOwnershipViewModel> CurrentOwnerships { get; set; } = [];
    }

    public sealed class PlotOwnershipViewModel
    {
        public int MemberId { get; init; }

        public string MemberFullName { get; init; } = string.Empty;

        public decimal? OwnershipShare { get; init; }

        public bool IsPrimaryContact { get; init; }

        public DateOnly? ValidFrom { get; init; }

        public DateOnly? ValidTo { get; init; }
    }
}
