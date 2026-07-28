using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Association;

[Authorize(Roles = RoleNames.Administrator)]
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAssociationElectricityService _associationElectricityService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateModel(ApplicationDbContext dbContext, IAssociationElectricityService associationElectricityService, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _associationElectricityService = associationElectricityService;
        _userManager = userManager;
    }

    [BindProperty]
    public ReadingInputModel Input { get; set; } = new();

    public PreviousReadingViewModel? PreviousReading { get; private set; }

    public PreviewViewModel Preview { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        PreviousReading = await GetPreviousReadingAsync(cancellationToken);
        if (PreviousReading is null)
        {
            TempData["ErrorMessage"] = "Сначала внесите начальные показания общего счётчика.";
            return RedirectToPage("/Administration/Electricity/Association/Initial");
        }

        Input.ReadingDate = GetDefaultReadingDate(PreviousReading.ReadingDate);
        Preview = await BuildPreviewAsync(Input.ReadingDate.Value, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        PreviousReading = await GetPreviousReadingAsync(cancellationToken);
        if (PreviousReading is null)
        {
            TempData["ErrorMessage"] = "Сначала внесите начальные показания общего счётчика.";
            return RedirectToPage("/Administration/Electricity/Association/Initial");
        }

        Preview = await BuildPreviewAsync(Input.ReadingDate ?? GetDefaultReadingDate(PreviousReading.ReadingDate), cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var result = await _associationElectricityService.CreateReadingAsync(
            new CreateAssociationElectricityReadingRequest(
                Input.ReadingDate!.Value,
                Input.CurrentDayReading!.Value,
                Input.CurrentNightReading!.Value,
                currentUser?.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Не удалось сохранить показания общего счётчика.");
            return Page();
        }

        TempData["SuccessMessage"] = "Показания общего счётчика сохранены.";
        return RedirectToPage("/Administration/Electricity/Association/Index");
    }

    private async Task<PreviousReadingViewModel?> GetPreviousReadingAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.AssociationElectricityReadings
            .AsNoTracking()
            .OrderByDescending(reading => reading.ReadingDate)
            .ThenByDescending(reading => reading.Id)
            .Select(reading => new PreviousReadingViewModel
            {
                ReadingDate = reading.ReadingDate,
                CurrentDayReading = reading.CurrentDayReading,
                CurrentNightReading = reading.CurrentNightReading
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<PreviewViewModel> BuildPreviewAsync(DateOnly readingDate, CancellationToken cancellationToken)
    {
        var tariff = await _dbContext.AssociationElectricityTariffs
            .AsNoTracking()
            .Where(item => item.EffectiveFrom <= readingDate)
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenByDescending(item => item.Id)
            .Select(item => new TariffViewModel
            {
                EffectiveFrom = item.EffectiveFrom,
                DayRate = item.DayRate,
                NightRate = item.NightRate
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (PreviousReading is null)
        {
            return new PreviewViewModel { Tariff = tariff };
        }

        var dayConsumption = Input.CurrentDayReading.HasValue ? Input.CurrentDayReading.Value - PreviousReading.CurrentDayReading : (decimal?)null;
        var nightConsumption = Input.CurrentNightReading.HasValue ? Input.CurrentNightReading.Value - PreviousReading.CurrentNightReading : (decimal?)null;
        var dayAmount = tariff is not null && dayConsumption.HasValue && dayConsumption.Value >= 0m
            ? Math.Round(dayConsumption.Value * tariff.DayRate, 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;
        var nightAmount = tariff is not null && nightConsumption.HasValue && nightConsumption.Value >= 0m
            ? Math.Round(nightConsumption.Value * tariff.NightRate, 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        return new PreviewViewModel
        {
            Tariff = tariff,
            DayConsumption = dayConsumption,
            NightConsumption = nightConsumption,
            TotalConsumption = dayConsumption.HasValue && nightConsumption.HasValue ? dayConsumption.Value + nightConsumption.Value : null,
            TotalAmount = dayAmount.HasValue && nightAmount.HasValue ? dayAmount.Value + nightAmount.Value : null
        };
    }

    private static DateOnly GetDefaultReadingDate(DateOnly previousDate)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return today <= previousDate ? previousDate.AddDays(1) : today;
    }

    public sealed class PreviousReadingViewModel
    {
        public DateOnly ReadingDate { get; init; }
        public decimal CurrentDayReading { get; init; }
        public decimal CurrentNightReading { get; init; }
    }

    public sealed class TariffViewModel
    {
        public DateOnly EffectiveFrom { get; init; }
        public decimal DayRate { get; init; }
        public decimal NightRate { get; init; }
    }

    public sealed class PreviewViewModel
    {
        public TariffViewModel? Tariff { get; init; }
        public decimal? DayConsumption { get; init; }
        public decimal? NightConsumption { get; init; }
        public decimal? TotalConsumption { get; init; }
        public decimal? TotalAmount { get; init; }
    }
}
