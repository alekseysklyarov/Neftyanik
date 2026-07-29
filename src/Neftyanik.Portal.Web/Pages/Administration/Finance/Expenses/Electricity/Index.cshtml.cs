using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using ReadingInputModel = Neftyanik.Portal.Web.Pages.Administration.Electricity.Association.ReadingInputModel;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses.Electricity;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAssociationElectricityService _associationElectricityService;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(
        ApplicationDbContext dbContext,
        IAssociationElectricityService associationElectricityService,
        UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _associationElectricityService = associationElectricityService;
        _userManager = userManager;
    }

    [BindProperty]
    public ReadingInputModel Input { get; set; } = new();

    public bool HasHistory => Readings.Count > 0;

    public PreviousReadingViewModel? PreviousReading { get; private set; }

    public PreviewViewModel Preview { get; private set; } = new();

    public IReadOnlyList<ReadingItemViewModel> Readings { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPageStateAsync(cancellationToken);

        if (!HasHistory)
        {
            Input.ReadingDate = DateOnly.FromDateTime(DateTime.Today);
            return;
        }

        Input.ReadingDate = GetDefaultReadingDate(PreviousReading!.ReadingDate);
        Preview = await BuildPreviewAsync(Input.ReadingDate.Value, cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadPageStateAsync(cancellationToken);

        if (HasHistory)
        {
            Preview = await BuildPreviewAsync(Input.ReadingDate ?? GetDefaultReadingDate(PreviousReading!.ReadingDate), cancellationToken);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser is null)
        {
            return Challenge();
        }

        if (!HasHistory)
        {
            var initialResult = await _associationElectricityService.CreateInitialReadingAsync(
                new CreateAssociationElectricityInitialReadingRequest(
                    Input.ReadingDate!.Value,
                    Input.CurrentDayReading!.Value,
                    Input.CurrentNightReading!.Value,
                    currentUser.Id),
                cancellationToken);

            if (!initialResult.Succeeded)
            {
                ModelState.AddModelError(string.Empty, initialResult.ErrorMessage ?? "Не удалось сохранить начальные показания.");
                return Page();
            }

            TempData["SuccessMessage"] = "Начальные показания общего счётчика сохранены.";
            return RedirectToPage();
        }

        var result = await _associationElectricityService.CreateReadingAsync(
            new CreateAssociationElectricityReadingRequest(
                Input.ReadingDate!.Value,
                Input.CurrentDayReading!.Value,
                Input.CurrentNightReading!.Value,
                currentUser.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Не удалось сохранить показания общего счётчика.");
            return Page();
        }

        TempData["SuccessMessage"] = "Показания общего счётчика сохранены.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPayAsync(long readingId, CancellationToken cancellationToken)
    {
        await LoadPageStateAsync(cancellationToken);

        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser is null)
        {
            return Challenge();
        }

        var result = await _associationElectricityService.CreateExpenseAsync(
            new CreateAssociationElectricityExpenseRequest(readingId, currentUser.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.ErrorMessage ?? "Не удалось создать расход по общему счётчику.";
            return RedirectToPage();
        }

        TempData["SuccessMessage"] = $"Расход по электроэнергии создан: {result.TotalAmount:0.00} грн.";
        return RedirectToPage();
    }

    private async Task LoadPageStateAsync(CancellationToken cancellationToken)
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
                IsInitialReading = reading.IsInitialReading,
                HasExpense = reading.SupplierExpense != null,
                ExpenseId = reading.SupplierExpense != null ? reading.SupplierExpense.Id : null
            })
            .ToListAsync(cancellationToken);

        PreviousReading = Readings.Count == 0
            ? null
            : new PreviousReadingViewModel
            {
                ReadingDate = Readings[0].ReadingDate,
                CurrentDayReading = Readings[0].CurrentDayReading,
                CurrentNightReading = Readings[0].CurrentNightReading
            };
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

        tariff ??= await _dbContext.ElectricityTariffs
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
        public bool HasExpense { get; init; }
        public long? ExpenseId { get; init; }
    }
}
