using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Association;

[Authorize(Roles = RoleNames.Administrator)]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<ReadingItemViewModel> Readings { get; private set; } = [];

    public bool HasHistory => Readings.Count > 0;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Readings = await _dbContext.AssociationElectricityReadings
            .AsNoTracking()
            .OrderByDescending(reading => reading.ReadingDate)
            .ThenByDescending(reading => reading.Id)
            .Select(reading => new ReadingItemViewModel
            {
                Id = reading.Id,
                ReadingDate = reading.ReadingDate,
                CurrentDayReading = reading.CurrentDayReading,
                DayConsumption = reading.DayConsumption,
                CurrentNightReading = reading.CurrentNightReading,
                NightConsumption = reading.NightConsumption,
                TotalConsumption = reading.TotalConsumption,
                AppliedSupplierDayRate = reading.AppliedSupplierDayRate,
                AppliedSupplierNightRate = reading.AppliedSupplierNightRate,
                TotalSupplierAmount = reading.TotalSupplierAmount,
                IsInitialReading = reading.IsInitialReading
            })
            .ToListAsync(cancellationToken);
    }

    public sealed class ReadingItemViewModel
    {
        public long Id { get; init; }
        public DateOnly ReadingDate { get; init; }
        public decimal CurrentDayReading { get; init; }
        public decimal? DayConsumption { get; init; }
        public decimal CurrentNightReading { get; init; }
        public decimal? NightConsumption { get; init; }
        public decimal? TotalConsumption { get; init; }
        public decimal? AppliedSupplierDayRate { get; init; }
        public decimal? AppliedSupplierNightRate { get; init; }
        public decimal? TotalSupplierAmount { get; init; }
        public bool IsInitialReading { get; init; }
    }
}
