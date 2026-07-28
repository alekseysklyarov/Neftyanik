using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;

namespace Neftyanik.Portal.Web.Pages.Member.Electricity.Meters.Readings;

[Authorize(Roles = RoleNames.Member)]
public class IndexModel : PageModel
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public MeterContextViewModel Meter { get; private set; } = new();

    public IReadOnlyList<ReadingItemViewModel> Readings { get; private set; } = [];

    public int TotalPages { get; private set; } = 1;

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public async Task<IActionResult> OnGetAsync(int meterId, CancellationToken cancellationToken)
    {
        var meter = await LoadOwnedMeterAsync(meterId, cancellationToken);
        if (meter is null)
        {
            return NotFound();
        }

        Meter = meter;
        PageNumber = PageNumber < 1 ? 1 : PageNumber;

        var query = _dbContext.MemberElectricityReadings
            .AsNoTracking()
            .Where(reading => reading.MemberElectricityMeterId == meterId)
            .OrderByDescending(reading => reading.ReadingDate)
            .ThenByDescending(reading => reading.Id)
            .Select(reading => new ReadingItemViewModel
            {
                ReadingDate = reading.ReadingDate,
                CurrentReading = reading.CurrentReading,
                Consumption = reading.Consumption,
                AppliedMemberRate = reading.AppliedMemberRate,
                Amount = reading.Amount,
                IsInitialReading = reading.IsInitialReading,
                IsChargeCancelled = reading.Charge != null && reading.Charge.CancelledAtUtc != null
            });

        var totalCount = await query.CountAsync(cancellationToken);
        TotalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)PageSize);
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        Readings = await query.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken);
        return Page();
    }

    private async Task<MeterContextViewModel?> LoadOwnedMeterAsync(int meterId, CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return null;
        }

        var memberId = await _dbContext.Members
            .AsNoTracking()
            .Where(member => member.ApplicationUserId == user.Id)
            .Select(member => (int?)member.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!memberId.HasValue)
        {
            return null;
        }

        var meter = await _dbContext.MemberElectricityMeters
            .AsNoTracking()
            .Where(item => item.Id == meterId && item.MemberId == memberId.Value)
            .Select(item => new MeterContextViewModel
            {
                Id = item.Id,
                DisplayName = !string.IsNullOrWhiteSpace(item.Name) ? item.Name : !string.IsNullOrWhiteSpace(item.MeterNumber) ? item.MeterNumber : $"Счетчик #{item.Id}"
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (meter is null)
        {
            return null;
        }

        var currentDate = DateOnly.FromDateTime(DateTime.Today);
        var activeOwnedPlotIds = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(memberId.Value, currentDate)
            .Select(ownership => ownership.PlotId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var linkedPlotIds = await _dbContext.MemberElectricityMeterPlots
            .AsNoTracking()
            .Where(link => link.MemberElectricityMeterId == meterId)
            .Select(link => link.PlotId)
            .ToListAsync(cancellationToken);

        if (linkedPlotIds.Any(plotId => !activeOwnedPlotIds.Contains(plotId)))
        {
            return null;
        }

        return meter;
    }

    public sealed class MeterContextViewModel
    {
        public int Id { get; init; }
        public string DisplayName { get; init; } = "—";
    }

    public sealed class ReadingItemViewModel
    {
        public DateOnly ReadingDate { get; init; }
        public decimal CurrentReading { get; init; }
        public decimal? Consumption { get; init; }
        public decimal? AppliedMemberRate { get; init; }
        public decimal? Amount { get; init; }
        public bool IsInitialReading { get; init; }
        public bool IsChargeCancelled { get; init; }
        public string ChargeStatusText => IsInitialReading ? "Без начисления" : IsChargeCancelled ? "Начисление отменено" : "Начислено";
    }
}
