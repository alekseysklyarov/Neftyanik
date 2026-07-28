using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Electricity.Tariffs;

[Authorize(Roles = RoleNames.Administrator)]
public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public DetailsModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public TariffDetailsViewModel Tariff { get; private set; } = new();

    public decimal? CurrentMemberRate { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var tariff = await _dbContext.ElectricityTariffs
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new TariffDetailsViewModel
            {
                Id = item.Id,
                EffectiveFrom = item.EffectiveFrom,
                DayRate = item.DayRate,
                NightRate = item.NightRate,
                CreatedAtUtc = item.CreatedAtUtc,
                CreatedBy = item.CreatedByUser != null
                    ? item.CreatedByUser.DisplayName ?? item.CreatedByUser.Email ?? item.CreatedByUser.UserName ?? "—"
                    : "—"
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (tariff is null)
        {
            return NotFound();
        }

        var nextEffectiveFrom = await _dbContext.ElectricityTariffs
            .AsNoTracking()
            .Where(item => item.EffectiveFrom > tariff.EffectiveFrom)
            .OrderBy(item => item.EffectiveFrom)
            .Select(item => (DateOnly?)item.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        tariff = tariff with
        {
            UsageCount = await _dbContext.ElectricityReadings
                .AsNoTracking()
                .CountAsync(reading => !reading.IsInitialReading
                    && reading.DayRate == tariff.DayRate
                    && reading.NightRate == tariff.NightRate
                    && reading.ReadingDate >= tariff.EffectiveFrom
                    && (!nextEffectiveFrom.HasValue || reading.ReadingDate < nextEffectiveFrom.Value), cancellationToken)
        };

        var today = DateOnly.FromDateTime(DateTime.Today);
        CurrentMemberRate = await _dbContext.MemberElectricityTariffs
            .AsNoTracking()
            .Where(item => item.EffectiveFrom <= today)
            .OrderByDescending(item => item.EffectiveFrom)
            .Select(item => (decimal?)item.Rate)
            .FirstOrDefaultAsync(cancellationToken);

        Tariff = tariff;
        return Page();
    }

    public sealed record TariffDetailsViewModel
    {
        public int Id { get; init; }

        public DateOnly EffectiveFrom { get; init; }

        public decimal DayRate { get; init; }

        public decimal NightRate { get; init; }

        public DateTimeOffset CreatedAtUtc { get; init; }

        public string CreatedBy { get; init; } = "—";

        public int UsageCount { get; init; }
    }
}
