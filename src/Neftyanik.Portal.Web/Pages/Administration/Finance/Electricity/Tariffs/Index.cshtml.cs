using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Electricity.Tariffs;

[Authorize(Roles = RoleNames.Administrator)]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<TariffListItemViewModel> Tariffs { get; private set; } = [];

    public decimal? CurrentMemberRate { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var currentEffectiveFrom = await _dbContext.ElectricityTariffs
            .AsNoTracking()
            .Where(tariff => tariff.EffectiveFrom <= today)
            .OrderByDescending(tariff => tariff.EffectiveFrom)
            .Select(tariff => (DateOnly?)tariff.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        CurrentMemberRate = await _dbContext.MemberElectricityTariffs
            .AsNoTracking()
            .Where(tariff => tariff.EffectiveFrom <= today)
            .OrderByDescending(tariff => tariff.EffectiveFrom)
            .Select(tariff => (decimal?)tariff.Rate)
            .FirstOrDefaultAsync(cancellationToken);

        Tariffs = await _dbContext.ElectricityTariffs
            .AsNoTracking()
            .OrderByDescending(tariff => tariff.EffectiveFrom)
            .ThenByDescending(tariff => tariff.Id)
            .Select(tariff => new TariffListItemViewModel
            {
                Id = tariff.Id,
                EffectiveFrom = tariff.EffectiveFrom,
                DayRate = tariff.DayRate,
                NightRate = tariff.NightRate,
                CreatedAtUtc = tariff.CreatedAtUtc,
                CreatedBy = tariff.CreatedByUser != null
                    ? tariff.CreatedByUser.DisplayName ?? tariff.CreatedByUser.Email ?? tariff.CreatedByUser.UserName ?? "—"
                    : "—",
                IsCurrent = currentEffectiveFrom.HasValue && tariff.EffectiveFrom == currentEffectiveFrom.Value
            })
            .ToListAsync(cancellationToken);
    }

    public sealed class TariffListItemViewModel
    {
        public int Id { get; init; }

        public DateOnly EffectiveFrom { get; init; }

        public decimal DayRate { get; init; }

        public decimal NightRate { get; init; }

        public bool IsCurrent { get; init; }

        public DateTimeOffset CreatedAtUtc { get; init; }

        public string CreatedBy { get; init; } = "—";
    }
}
