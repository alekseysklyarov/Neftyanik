using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Member.Electricity.Meters.Readings;

[Authorize(Roles = RoleNames.Member)]
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemberElectricityService _memberElectricityService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateModel(ApplicationDbContext dbContext, IMemberElectricityService memberElectricityService, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _memberElectricityService = memberElectricityService;
        _userManager = userManager;
    }

    [BindProperty]
    public Pages.Administration.Electricity.Meters.ReadingInputModel Input { get; set; } = new();

    public MeterContextViewModel Meter { get; private set; } = new();

    public PreviewViewModel Preview { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int meterId, CancellationToken cancellationToken)
    {
        var meter = await LoadOwnedMeterAsync(meterId, cancellationToken);
        if (meter is null)
        {
            return NotFound();
        }

        if (!meter.HasInitialReading)
        {
            TempData["ErrorMessage"] = AppLocalizer.Get("Начальные показания ещё не установлены администратором.", "Початкові показання ще не встановлені адміністратором.", "Initial readings have not been set by the administrator yet.");
            return RedirectToPage("/Member/Electricity/Index");
        }

        Meter = meter;
        Input.ReadingDate = GetDefaultReadingDate(meter.PreviousReadingDate!.Value);
        Preview = await BuildPreviewAsync(Input.ReadingDate.Value, meterId, meter.PreviousReading, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int meterId, CancellationToken cancellationToken)
    {
        var meter = await LoadOwnedMeterAsync(meterId, cancellationToken);
        if (meter is null)
        {
            return NotFound();
        }

        if (!meter.HasInitialReading)
        {
            TempData["ErrorMessage"] = AppLocalizer.Get("Начальные показания ещё не установлены администратором.", "Початкові показання ще не встановлені адміністратором.", "Initial readings have not been set by the administrator yet.");
            return RedirectToPage("/Member/Electricity/Index");
        }

        Meter = meter;
        Preview = await BuildPreviewAsync(Input.ReadingDate ?? GetDefaultReadingDate(meter.PreviousReadingDate!.Value), meterId, meter.PreviousReading, cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var result = await _memberElectricityService.CreateReadingAsync(
            new CreateMemberElectricityReadingRequest(
                meterId,
                Input.ReadingDate!.Value,
                Input.CurrentReading!.Value,
                currentUser?.Id,
                true),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? AppLocalizer.Get("Не удалось сохранить показания.", "Не вдалося зберегти показання.", "Failed to save the readings."));
            return Page();
        }

        TempData["SuccessMessage"] = AppLocalizer.Get("Показания сохранены.", "Показання збережено.", "Readings have been saved.");
        return RedirectToPage("/Member/Electricity/Meters/Readings/Index", new { meterId });
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
            .Where(item => item.Id == meterId && item.MemberId == memberId.Value && item.IsActive)
            .Select(item => new MeterContextViewModel
            {
                Id = item.Id,
                MemberId = item.MemberId,
                DisplayName = !string.IsNullOrWhiteSpace(item.Name) ? item.Name : !string.IsNullOrWhiteSpace(item.MeterNumber) ? item.MeterNumber : AppLocalizer.Get($"Счетчик #{item.Id}", $"Лічильник #{item.Id}", $"Meter #{item.Id}"),
                BillingPlotId = item.BillingPlotId,
                BillingPlotNumber = item.BillingPlot != null ? item.BillingPlot.Number : "—",
                LinkedPlotNumbers = item.Plots.OrderBy(plot => plot.Number)
                    .Select(plot => plot.Number)
                    .ToList(),
                PreviousReadingDate = item.Readings.OrderByDescending(reading => reading.ReadingDate).ThenByDescending(reading => reading.Id).Select(reading => (DateOnly?)reading.ReadingDate).FirstOrDefault(),
                PreviousReading = item.Readings.OrderByDescending(reading => reading.ReadingDate).ThenByDescending(reading => reading.Id).Select(reading => (decimal?)reading.CurrentReading).FirstOrDefault(),
                HasInitialReading = item.Readings.Any()
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

        var linkedPlotIds = await _dbContext.Plots
            .AsNoTracking()
            .Where(plot => plot.MemberElectricityMeterId == meterId)
            .Select(plot => plot.Id)
            .ToListAsync(cancellationToken);

        if (!linkedPlotIds.Contains(meter.BillingPlotId) || !activeOwnedPlotIds.Contains(meter.BillingPlotId) || linkedPlotIds.Any(plotId => !activeOwnedPlotIds.Contains(plotId)))
        {
            return null;
        }

        return meter;
    }

    private async Task<PreviewViewModel> BuildPreviewAsync(DateOnly readingDate, int meterId, decimal? previousReading, CancellationToken cancellationToken)
    {
        var tariff = await _dbContext.MemberElectricityTariffs
            .AsNoTracking()
            .Where(item => item.EffectiveFrom <= readingDate)
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenByDescending(item => item.Id)
            .Select(item => new TariffViewModel
            {
                EffectiveFrom = item.EffectiveFrom,
                Rate = item.Rate
            })
            .FirstOrDefaultAsync(cancellationToken);

        var consumption = Input.CurrentReading.HasValue && previousReading.HasValue
            ? Input.CurrentReading.Value - previousReading.Value
            : (decimal?)null;
        var amount = tariff is not null && consumption.HasValue && consumption.Value >= 0m
            ? Math.Round(consumption.Value * tariff.Rate, 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        return new PreviewViewModel
        {
            Tariff = tariff,
            Consumption = consumption,
            Amount = amount
        };
    }

    private static DateOnly GetDefaultReadingDate(DateOnly previousDate)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return today <= previousDate ? previousDate.AddDays(1) : today;
    }

    public sealed class MeterContextViewModel
    {
        public int Id { get; init; }
        public int MemberId { get; init; }
        public string DisplayName { get; init; } = "—";
        public int BillingPlotId { get; init; }
        public string BillingPlotNumber { get; init; } = "—";
        public IReadOnlyList<string> LinkedPlotNumbers { get; init; } = [];
        public DateOnly? PreviousReadingDate { get; init; }
        public decimal? PreviousReading { get; init; }
        public bool HasInitialReading { get; init; }
    }

    public sealed class TariffViewModel
    {
        public DateOnly EffectiveFrom { get; init; }
        public decimal Rate { get; init; }
    }

    public sealed class PreviewViewModel
    {
        public TariffViewModel? Tariff { get; init; }
        public decimal? Consumption { get; init; }
        public decimal? Amount { get; init; }
    }
}
