using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Association.Tariffs;

[Authorize(Roles = RoleNames.Administrator)]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<TariffViewModel> Tariffs { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var currentEffectiveFrom = await _dbContext.AssociationElectricityTariffs
            .AsNoTracking()
            .Where(item => item.EffectiveFrom <= today)
            .OrderByDescending(item => item.EffectiveFrom)
            .Select(item => (DateOnly?)item.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        Tariffs = await _dbContext.AssociationElectricityTariffs
            .AsNoTracking()
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenByDescending(item => item.Id)
            .Select(item => new TariffViewModel
            {
                EffectiveFrom = item.EffectiveFrom,
                DayRate = item.DayRate,
                NightRate = item.NightRate,
                CreatedAtUtc = item.CreatedAtUtc,
                CreatedBy = item.CreatedByUser != null ? item.CreatedByUser.DisplayName ?? item.CreatedByUser.Email ?? item.CreatedByUser.UserName ?? "—" : "—",
                IsCurrent = currentEffectiveFrom.HasValue && item.EffectiveFrom == currentEffectiveFrom.Value
            })
            .ToListAsync(cancellationToken);
    }

    public sealed class TariffViewModel
    {
        public DateOnly EffectiveFrom { get; init; }
        public decimal DayRate { get; init; }
        public decimal NightRate { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public string CreatedBy { get; init; } = "—";
        public bool IsCurrent { get; init; }
    }
}
