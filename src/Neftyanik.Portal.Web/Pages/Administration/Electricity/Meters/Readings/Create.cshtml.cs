using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters.Readings;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CreateModel : PageModel
{
    private readonly IMemberElectricityService _memberElectricityService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateModel(IMemberElectricityService memberElectricityService, UserManager<ApplicationUser> userManager)
    {
        _memberElectricityService = memberElectricityService;
        _userManager = userManager;
    }

    [BindProperty]
    public Pages.Administration.Electricity.Meters.ReadingInputModel Input { get; set; } = new();

    public MeterContextViewModel Meter { get; private set; } = new();

    public PreviewViewModel Preview { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var initialContext = await _memberElectricityService.GetReadingEntryContextAsync(
            id,
            DateOnly.FromDateTime(DateTime.Today),
            null,
            null,
            cancellationToken);

        if (initialContext is null)
        {
            return NotFound();
        }

        if (!initialContext.HasInitialReading)
        {
            TempData["ErrorMessage"] = "Сначала внесите начальные показания.";
            return RedirectToPage("/Administration/Electricity/Meters/Initial", new { id });
        }

        if (!ValidateMeterAccess(initialContext, out var redirectResult))
        {
            return redirectResult!;
        }

        Input.ReadingDate = GetDefaultReadingDate(initialContext.PreviousReadingDate!.Value);
        if (!await LoadPageStateAsync(id, Input.ReadingDate.Value, null, null, cancellationToken))
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        var readingDate = Input.ReadingDate ?? DateOnly.FromDateTime(DateTime.Today);
        if (!await LoadPageStateAsync(id, readingDate, Input.CurrentReading, Input.CurrentNightReading, cancellationToken))
        {
            return NotFound();
        }

        if (!Meter.HasInitialReading)
        {
            TempData["ErrorMessage"] = "Сначала внесите начальные показания.";
            return RedirectToPage("/Administration/Electricity/Meters/Initial", new { id });
        }

        AddMeterStateValidationErrors(Meter);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var result = await _memberElectricityService.CreateReadingAsync(
            new CreateMemberElectricityReadingRequest(
                id,
                Input.ReadingDate!.Value,
                Input.CurrentReading!.Value,
                Input.CurrentNightReading,
                currentUser?.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Не удалось сохранить показания.");
            return Page();
        }

        TempData["SuccessMessage"] = "Показания сохранены, начисление создано.";
        return RedirectToPage("/Administration/Electricity/Meters/Readings/Index", new { id });
    }

    private async Task<bool> LoadPageStateAsync(int id, DateOnly readingDate, decimal? currentReading, decimal? currentNightReading, CancellationToken cancellationToken)
    {
        var context = await _memberElectricityService.GetReadingEntryContextAsync(id, readingDate, currentReading, currentNightReading, cancellationToken);
        if (context is null)
        {
            return false;
        }

        Meter = new MeterContextViewModel
        {
            Id = context.MeterId,
            MemberName = context.MemberName,
            DisplayName = context.DisplayName,
            MeterType = context.MeterType,
            BillingPlotId = context.BillingPlotId,
            BillingPlotNumber = context.BillingPlotNumber,
            LinkedPlotNumbers = context.LinkedPlotNumbers,
            LinkedPlotIds = context.LinkedPlotIds,
            PreviousReadingDate = context.PreviousReadingDate,
            PreviousReading = context.PreviousReading,
            PreviousNightReading = context.PreviousNightReading,
            HasInitialReading = context.HasInitialReading,
            IsActive = context.IsActive,
            BillingPlotIsLinked = context.BillingPlotIsLinked,
            BillingPlotIsOwnedByMember = context.BillingPlotIsOwnedByMember
        };

        Preview = new PreviewViewModel
        {
            Tariff = context.Tariff is null ? null : new TariffViewModel
            {
                EffectiveFrom = context.Tariff.EffectiveFrom,
                Rate = context.Tariff.Rate,
                NightRate = context.Tariff.NightRate
            },
            Consumption = context.Consumption,
            Amount = context.Amount
        };

        return true;
    }

    private bool ValidateMeterAccess(MemberElectricityReadingEntryContext meter, out IActionResult? redirectResult)
    {
        if (!meter.IsActive)
        {
            TempData["ErrorMessage"] = "Счётчик неактивен. Внести новые показания нельзя.";
            redirectResult = RedirectToPage("/Administration/Electricity/Meters/Details", new { id = meter.MeterId });
            return false;
        }

        if (!meter.BillingPlotIsLinked)
        {
            TempData["ErrorMessage"] = "Расчётный участок больше не привязан к счётчику.";
            redirectResult = RedirectToPage("/Administration/Electricity/Meters/Details", new { id = meter.MeterId });
            return false;
        }

        if (!meter.BillingPlotIsOwnedByMember)
        {
            TempData["ErrorMessage"] = "Расчётный участок больше не принадлежит владельцу счётчика.";
            redirectResult = RedirectToPage("/Administration/Electricity/Meters/Details", new { id = meter.MeterId });
            return false;
        }

        redirectResult = null;
        return true;
    }

    private void AddMeterStateValidationErrors(MeterContextViewModel meter)
    {
        if (!meter.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Счётчик неактивен. Внести новые показания нельзя.");
        }

        if (!meter.BillingPlotIsLinked)
        {
            ModelState.AddModelError(string.Empty, "Расчётный участок больше не привязан к счётчику.");
        }

        if (!meter.BillingPlotIsOwnedByMember)
        {
            ModelState.AddModelError(string.Empty, "Расчётный участок больше не принадлежит владельцу счётчика.");
        }
    }

    private static DateOnly GetDefaultReadingDate(DateOnly previousDate)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return today <= previousDate ? previousDate.AddDays(1) : today;
    }

    public sealed class MeterContextViewModel
    {
        public int Id { get; init; }
        public string MemberName { get; init; } = "—";
        public string DisplayName { get; init; } = "—";
        public MemberElectricityMeterType MeterType { get; init; } = MemberElectricityMeterType.SingleRate;
        public int BillingPlotId { get; init; }
        public string BillingPlotNumber { get; init; } = "—";
        public IReadOnlyList<string> LinkedPlotNumbers { get; init; } = [];
        public IReadOnlyList<int> LinkedPlotIds { get; init; } = [];
        public DateOnly? PreviousReadingDate { get; init; }
        public decimal? PreviousReading { get; init; }
        public decimal? PreviousNightReading { get; init; }
        public bool HasInitialReading { get; init; }
        public bool IsActive { get; init; }
        public bool BillingPlotIsLinked { get; init; }
        public bool BillingPlotIsOwnedByMember { get; set; }
        public bool RequiresNightReading => MeterType == MemberElectricityMeterType.DayNight;
    }

    public sealed class TariffViewModel
    {
        public DateOnly EffectiveFrom { get; init; }
        public decimal Rate { get; init; }
        public decimal? NightRate { get; init; }
    }

    public sealed class PreviewViewModel
    {
        public TariffViewModel? Tariff { get; init; }
        public decimal? Consumption { get; init; }
        public decimal? Amount { get; init; }
    }
}
