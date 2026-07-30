using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class InitialModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemberElectricityService _memberElectricityService;
    private readonly UserManager<ApplicationUser> _userManager;

    public InitialModel(ApplicationDbContext dbContext, IMemberElectricityService memberElectricityService, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _memberElectricityService = memberElectricityService;
        _userManager = userManager;
    }

    [BindProperty]
    public ReadingInputModel Input { get; set; } = new();

    public MeterContextViewModel Meter { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var meter = await LoadMeterAsync(id, cancellationToken);
        if (meter is null)
        {
            return NotFound();
        }

        if (meter.HasReadings)
        {
            TempData["ErrorMessage"] = "Начальные показания уже внесены.";
            return RedirectToPage("/Administration/Electricity/Meters/Details", new { id });
        }

        Meter = meter;
        Input.ReadingDate = DateOnly.FromDateTime(DateTime.Today);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        var meter = await LoadMeterAsync(id, cancellationToken);
        if (meter is null)
        {
            return NotFound();
        }

        Meter = meter;

        if (meter.HasReadings)
        {
            ModelState.AddModelError(string.Empty, "Начальные показания уже внесены.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var result = await _memberElectricityService.CreateInitialReadingAsync(
            new CreateMemberElectricityInitialReadingRequest(
                id,
                Input.ReadingDate!.Value,
                Input.CurrentReading!.Value,
                Input.CurrentNightReading,
                currentUser?.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            var errorKey = result.ErrorMessage?.Contains("ночн", StringComparison.OrdinalIgnoreCase) == true
                ? $"{nameof(Input)}.{nameof(ReadingInputModel.CurrentNightReading)}"
                : string.Empty;
            ModelState.AddModelError(errorKey, result.ErrorMessage ?? "Не удалось сохранить начальные показания.");
            return Page();
        }

        TempData["SuccessMessage"] = "Начальные показания сохранены без создания начисления.";
        return RedirectToPage("/Administration/Electricity/Meters/Details", new { id });
    }

    private async Task<MeterContextViewModel?> LoadMeterAsync(int id, CancellationToken cancellationToken)
    {
        return await _dbContext.MemberElectricityMeters
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new MeterContextViewModel
            {
                Id = item.Id,
                DisplayName = !string.IsNullOrWhiteSpace(item.Name) ? item.Name : !string.IsNullOrWhiteSpace(item.MeterNumber) ? item.MeterNumber : $"Счетчик #{item.Id}",
                MemberName = item.Member != null ? item.Member.FullName : "—",
                BillingPlotNumber = item.BillingPlot != null ? item.BillingPlot.Number : "—",
                LinkedPlotNumbers = item.Plots.OrderBy(plot => plot.Number)
                    .Select(plot => plot.Number)
                    .ToList(),
                MeterType = item.Member != null ? item.Member.ElectricityMeterType : MemberElectricityMeterType.SingleRate,
                HasReadings = item.Readings.Any()
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public sealed class MeterContextViewModel
    {
        public int Id { get; init; }
        public string DisplayName { get; init; } = "—";
        public string MemberName { get; init; } = "—";
        public string BillingPlotNumber { get; init; } = "—";
        public IReadOnlyList<string> LinkedPlotNumbers { get; init; } = [];
        public MemberElectricityMeterType MeterType { get; init; } = MemberElectricityMeterType.SingleRate;
        public bool HasReadings { get; init; }
        public bool RequiresNightReading => MeterType == MemberElectricityMeterType.DayNight;
    }
}
