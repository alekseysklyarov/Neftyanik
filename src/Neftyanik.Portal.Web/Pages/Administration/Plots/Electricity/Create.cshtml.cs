using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Administration.Plots;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Electricity;

public class CreateModel : PlotPageModelBase
{
    private readonly IElectricityAccountingService _electricityAccountingService;

    public CreateModel(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IElectricityAccountingService electricityAccountingService)
        : base(dbContext, userManager)
    {
        _electricityAccountingService = electricityAccountingService;
    }

    [BindProperty]
    public ElectricityReadingInputModel Input { get; set; } = new();

    public PlotContextViewModel Plot { get; private set; } = new();

    public PreviousReadingViewModel PreviousReading { get; private set; } = new();

    public PreviewViewModel Preview { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int plotId, CancellationToken cancellationToken)
    {
        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        var previousReading = await GetLatestReadingAsync(plotId, cancellationToken);
        if (previousReading is null)
        {
            TempData["ErrorMessage"] = "Сначала внесите начальные показания.";
            return RedirectToPage("/Administration/Plots/Electricity/Initial", new { plotId });
        }

        Plot = plot;
        PreviousReading = previousReading;
        Input.ReadingDate = GetDefaultReadingDate(previousReading.ReadingDate);
        Preview = await BuildPreviewAsync(Input.ReadingDate.Value, previousReading, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int plotId, CancellationToken cancellationToken)
    {
        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        var previousReading = await GetLatestReadingAsync(plotId, cancellationToken);
        if (previousReading is null)
        {
            TempData["ErrorMessage"] = "Сначала внесите начальные показания.";
            return RedirectToPage("/Administration/Plots/Electricity/Initial", new { plotId });
        }

        Plot = plot;
        PreviousReading = previousReading;
        Preview = await BuildPreviewAsync(Input.ReadingDate ?? GetDefaultReadingDate(previousReading.ReadingDate), previousReading, cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await UserManager.GetUserAsync(User);
        var result = await _electricityAccountingService.CreateReadingAsync(
            new CreateElectricityReadingRequest(
                plotId,
                Input.ReadingDate!.Value,
                Input.CurrentDayReading!.Value,
                Input.CurrentNightReading!.Value,
                currentUser?.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Не удалось создать начисление по электроэнергии.");
            Preview = await BuildPreviewAsync(Input.ReadingDate.Value, previousReading, cancellationToken);
            return Page();
        }

        TempData["SuccessMessage"] = "Показания сохранены, начисление по электроэнергии создано.";
        return RedirectToPage("/Administration/Plots/Electricity/Index", new { plotId });
    }

    private async Task<PreviousReadingViewModel?> GetLatestReadingAsync(int plotId, CancellationToken cancellationToken)
    {
        return await DbContext.ElectricityReadings
            .AsNoTracking()
            .Where(reading => reading.PlotId == plotId)
            .OrderByDescending(reading => reading.ReadingDate)
            .ThenByDescending(reading => reading.Id)
            .Select(reading => new PreviousReadingViewModel
            {
                ReadingDate = reading.ReadingDate,
                CurrentDayReading = reading.CurrentDayReading,
                CurrentNightReading = reading.CurrentNightReading,
                IsInitialReading = reading.IsInitialReading
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<PreviewViewModel> BuildPreviewAsync(DateOnly readingDate, PreviousReadingViewModel previousReading, CancellationToken cancellationToken)
    {
        var tariff = await DbContext.ElectricityTariffs
            .AsNoTracking()
            .Where(item => item.EffectiveFrom <= readingDate)
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenByDescending(item => item.Id)
            .Select(item => new TariffPreviewViewModel
            {
                DayRate = item.DayRate,
                NightRate = item.NightRate,
                EffectiveFrom = item.EffectiveFrom
            })
            .FirstOrDefaultAsync(cancellationToken);

        var currentDayReading = Input.CurrentDayReading;
        var currentNightReading = Input.CurrentNightReading;
        var dayConsumption = currentDayReading.HasValue ? currentDayReading.Value - previousReading.CurrentDayReading : (decimal?)null;
        var nightConsumption = currentNightReading.HasValue ? currentNightReading.Value - previousReading.CurrentNightReading : (decimal?)null;
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
            DayAmount = dayAmount,
            NightAmount = nightAmount,
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

        public bool IsInitialReading { get; init; }
    }

    public sealed class PreviewViewModel
    {
        public TariffPreviewViewModel? Tariff { get; init; }

        public decimal? DayConsumption { get; init; }

        public decimal? NightConsumption { get; init; }

        public decimal? DayAmount { get; init; }

        public decimal? NightAmount { get; init; }

        public decimal? TotalAmount { get; init; }
    }

    public sealed class TariffPreviewViewModel
    {
        public DateOnly EffectiveFrom { get; init; }

        public decimal DayRate { get; init; }

        public decimal NightRate { get; init; }
    }
}
