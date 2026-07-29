using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public DetailsModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public PlotDetailsViewModel Plot { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? ChargeTypeId { get; set; }

    public IReadOnlyList<SelectListItem> ChargeTypeOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);

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
                UpdatedAtUtc = item.UpdatedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (plot is null)
        {
            return NotFound();
        }

        var currentOwnerships = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .Where(ownership => ownership.PlotId == id
                && (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate))
            .OrderBy(ownership => ownership.Member != null ? ownership.Member.FullName : string.Empty)
            .Select(ownership => new PlotOwnershipViewModel
            {
                MemberId = ownership.MemberId,
                MemberFullName = ownership.Member != null ? ownership.Member.FullName : "—",
                OwnershipShare = ownership.OwnershipShare,
                ValidFrom = ownership.ValidFrom,
                ValidTo = ownership.ValidTo
            })
            .ToListAsync(cancellationToken);

        plot.CurrentOwnerships = currentOwnerships;
        plot.OwnersCount = currentOwnerships.Count;
        plot.SpecifiedTotalShare = currentOwnerships
            .Where(ownership => ownership.OwnershipShare.HasValue)
            .Sum(ownership => ownership.OwnershipShare ?? 0m);

        ChargeTypeOptions = await _dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.PlotId == id)
            .Select(charge => new
            {
                Id = charge.ChargeTypeId,
                Name = charge.ChargeType != null ? charge.ChargeType.Name : null
            })
            .Distinct()
            .OrderBy(item => item.Name)
            .Select(item => new SelectListItem
            {
                Value = item.Id.ToString(),
                Text = string.IsNullOrWhiteSpace(item.Name) ? AppLocalizer.Get($"Тип #{item.Id}", $"Тип #{item.Id}", $"Type #{item.Id}") : item.Name
            })
            .ToListAsync(cancellationToken);

        if (ChargeTypeId.HasValue && !ChargeTypeOptions.Any(option => option.Value == ChargeTypeId.Value.ToString()))
        {
            ChargeTypeId = null;
        }

        var chargesQuery = _dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.PlotId == id);

        if (ChargeTypeId.HasValue)
        {
            chargesQuery = chargesQuery.Where(charge => charge.ChargeTypeId == ChargeTypeId.Value);
        }

        plot.Charges = await chargesQuery
            .OrderByDescending(charge => charge.ChargeDate)
            .ThenByDescending(charge => charge.Id)
            .Select(charge => new PlotChargeViewModel
            {
                ChargeDate = charge.ChargeDate,
                ChargeTypeName = charge.ChargeType != null ? charge.ChargeType.Name : "—",
                Amount = charge.Amount,
                DueDate = charge.DueDate,
                Description = charge.Description,
                IsCancelled = charge.CancelledAtUtc != null,
                CancellationReason = charge.CancellationReason
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

        public int OwnersCount { get; set; }

        public decimal SpecifiedTotalShare { get; set; }

        public IReadOnlyList<PlotOwnershipViewModel> CurrentOwnerships { get; set; } = [];

        public IReadOnlyList<PlotChargeViewModel> Charges { get; set; } = [];
    }

    public sealed class PlotOwnershipViewModel
    {
        public int MemberId { get; init; }

        public string MemberFullName { get; init; } = string.Empty;

        public decimal? OwnershipShare { get; init; }

        public DateOnly? ValidFrom { get; init; }

        public DateOnly? ValidTo { get; init; }
    }

    public sealed class PlotChargeViewModel
    {
        public DateOnly ChargeDate { get; init; }

        public string ChargeTypeName { get; init; } = string.Empty;

        public decimal Amount { get; init; }

        public DateOnly? DueDate { get; init; }

        public string? Description { get; init; }

        public bool IsCancelled { get; init; }

        public string? CancellationReason { get; init; }

        public string StatusText => IsCancelled
            ? AppLocalizer.Get("Аннулирован", "Скасований", "Cancelled")
            : AppLocalizer.Get("Активный", "Активний", "Active");
    }
}
