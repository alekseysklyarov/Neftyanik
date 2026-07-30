using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Member.Electricity;

[Authorize(Roles = RoleNames.Member)]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    public IReadOnlyList<MeterItemViewModel> Meters { get; private set; } = [];

    public bool IsLinked { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (user.MustChangePassword)
        {
            return RedirectToPage("/Account/ChangeInitialPassword");
        }

        var memberId = await _dbContext.Members
            .AsNoTracking()
            .Where(member => member.ApplicationUserId == user.Id)
            .Select(member => (int?)member.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!memberId.HasValue)
        {
            IsLinked = false;
            return Page();
        }

        IsLinked = true;
        Meters = await _dbContext.MemberElectricityMeters
            .AsNoTracking()
            .Where(meter => meter.MemberId == memberId.Value && meter.IsActive)
            .OrderBy(meter => meter.Name)
            .ThenBy(meter => meter.MeterNumber)
            .Select(meter => new MeterItemViewModel
            {
                Id = meter.Id,
                Name = meter.Name,
                MeterNumber = meter.MeterNumber,
                BillingPlotNumber = meter.BillingPlot != null ? meter.BillingPlot.Number : "—",
                ConnectedPlots = meter.Plots.OrderBy(plot => plot.Number)
                    .Select(plot => plot.Number)
                    .ToList(),
                LatestReading = meter.Readings.OrderByDescending(reading => reading.ReadingDate).ThenByDescending(reading => reading.Id).Select(reading => (decimal?)reading.CurrentReading).FirstOrDefault(),
                LatestReadingDate = meter.Readings.OrderByDescending(reading => reading.ReadingDate).ThenByDescending(reading => reading.Id).Select(reading => (DateOnly?)reading.ReadingDate).FirstOrDefault(),
                LatestChargeAmount = meter.Readings.OrderByDescending(reading => reading.ReadingDate).ThenByDescending(reading => reading.Id).Select(reading => reading.Amount).FirstOrDefault(),
                HasInitialReading = meter.Readings.Any()
            })
            .ToListAsync(cancellationToken);

        var meterIds = Meters.Select(meter => meter.Id).ToArray();
        var latestReadingsByMeterId = meterIds.Length == 0
            ? new Dictionary<int, List<ReadingSnapshot>>()
            : (await _dbContext.MemberElectricityReadings
                .AsNoTracking()
                .Where(reading => meterIds.Contains(reading.MemberElectricityMeterId))
                .OrderByDescending(reading => reading.ReadingDate)
                .ThenByDescending(reading => reading.Id)
                .Select(reading => new ReadingSnapshot(
                    reading.MemberElectricityMeterId,
                    reading.CurrentReading,
                    reading.IsInitialReading))
                .ToListAsync(cancellationToken))
                .GroupBy(reading => reading.MemberElectricityMeterId)
                .ToDictionary(group => group.Key, group => group.Take(2).ToList());

        Meters = Meters
            .Select(meter =>
            {
                var readings = latestReadingsByMeterId.GetValueOrDefault(meter.Id, []);
                var latestReading = readings.FirstOrDefault();
                var previousReading = readings.Skip(1).FirstOrDefault();
                var latestConsumption = latestReading is not null && !latestReading.IsInitialReading && previousReading is not null
                    ? latestReading.CurrentReading - previousReading.CurrentReading
                    : (decimal?)null;

                return new MeterItemViewModel
                {
                    Id = meter.Id,
                    Name = meter.Name,
                    MeterNumber = meter.MeterNumber,
                    BillingPlotNumber = meter.BillingPlotNumber,
                    ConnectedPlots = meter.ConnectedPlots,
                    LatestReading = meter.LatestReading,
                    LatestReadingDate = meter.LatestReadingDate,
                    LatestConsumption = latestConsumption,
                    LatestChargeAmount = meter.LatestChargeAmount,
                    HasInitialReading = meter.HasInitialReading
                };
            })
            .ToList();

        return Page();
    }

    private sealed record ReadingSnapshot(int MemberElectricityMeterId, decimal CurrentReading, bool IsInitialReading);

    public sealed record MeterItemViewModel
    {
        public int Id { get; init; }
        public string? Name { get; init; }
        public string? MeterNumber { get; init; }
        public string BillingPlotNumber { get; init; } = "—";
        public IReadOnlyList<string> ConnectedPlots { get; init; } = [];
        public decimal? LatestReading { get; init; }
        public DateOnly? LatestReadingDate { get; init; }
        public decimal? LatestConsumption { get; init; }
        public decimal? LatestChargeAmount { get; init; }
        public bool HasInitialReading { get; init; }
        public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : !string.IsNullOrWhiteSpace(MeterNumber) ? MeterNumber : AppLocalizer.Get($"Счетчик #{Id}", $"Лічильник #{Id}", $"Meter #{Id}");
        public string StatusMessage => HasInitialReading ? string.Empty : AppLocalizer.Get("Начальные показания ещё не установлены администратором.", "Початкові показання ще не встановлені адміністратором.", "Initial readings have not been set by the administrator yet.");
    }
}
