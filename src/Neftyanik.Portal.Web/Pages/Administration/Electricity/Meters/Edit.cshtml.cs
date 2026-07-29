using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;

namespace Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemberElectricityService _memberElectricityService;
    private readonly UserManager<ApplicationUser> _userManager;

    public EditModel(ApplicationDbContext dbContext, IMemberElectricityService memberElectricityService, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _memberElectricityService = memberElectricityService;
        _userManager = userManager;
    }

    [BindProperty]
    public MeterInputModel Input { get; set; } = new();

    public int MeterId { get; private set; }

    public string MemberName { get; private set; } = "—";

    public bool HasReadingHistory { get; private set; }

    public IReadOnlyList<SelectListItem> PlotOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var meter = await _dbContext.MemberElectricityMeters
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.MemberId,
                MemberName = item.Member != null ? item.Member.FullName : "—",
                item.MeterNumber,
                item.Name,
                item.IsActive,
                item.BillingPlotId,
                PlotIds = item.MeterPlots.Select(link => link.PlotId).ToList(),
                HasReadingHistory = item.Readings.Any()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (meter is null)
        {
            return NotFound();
        }

        MeterId = meter.Id;
        MemberName = meter.MemberName;
        HasReadingHistory = meter.HasReadingHistory;
        Input = new MeterInputModel
        {
            MemberId = meter.MemberId,
            MeterNumber = meter.MeterNumber,
            Name = meter.Name,
            IsActive = meter.IsActive,
            BillingPlotId = meter.BillingPlotId,
            PlotIds = meter.PlotIds
        };

        await LoadPlotOptionsAsync(meter.MemberId, meter.Id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        var currentMeter = await _dbContext.MemberElectricityMeters
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.MemberId,
                MemberName = item.Member != null ? item.Member.FullName : "—",
                HasReadingHistory = item.Readings.Any()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (currentMeter is null)
        {
            return NotFound();
        }

        MeterId = currentMeter.Id;
        MemberName = currentMeter.MemberName;
        HasReadingHistory = currentMeter.HasReadingHistory;
        Input.MemberId = currentMeter.MemberId;
        await LoadPlotOptionsAsync(currentMeter.MemberId, currentMeter.Id, cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);

        var result = await _memberElectricityService.UpdateMeterAsync(
            new UpdateMemberElectricityMeterRequest(
                id,
                currentMeter.MemberId,
                Input.MeterNumber,
                Input.Name,
                Input.IsActive,
                Input.BillingPlotId!.Value,
                Input.PlotIds,
                currentUser?.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Не удалось обновить счётчик.");
            return Page();
        }

        TempData["SuccessMessage"] = "Счётчик обновлён.";
        return RedirectToPage("/Administration/Electricity/Meters/Details", new { id });
    }

    private async Task LoadPlotOptionsAsync(int memberId, int meterId, CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Today);
        var currentOwnedPlots = _dbContext.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(memberId, currentDate)
            .Select(ownership => new SelectListItem
            {
                Value = ownership.PlotId.ToString(),
                Text = ownership.Plot != null ? $"{ownership.Plot.Number} — {ownership.Plot.Address}" : ownership.PlotId.ToString()
            });

        var currentlyLinkedPlots = _dbContext.MemberElectricityMeterPlots
            .AsNoTracking()
            .Where(link => link.MemberElectricityMeterId == meterId)
            .Select(link => new SelectListItem
            {
                Value = link.PlotId.ToString(),
                Text = link.Plot != null
                    ? $"{link.Plot.Number} — {link.Plot.Address} (сейчас не принадлежит участнику)"
                    : $"{link.PlotId} (сейчас не принадлежит участнику)"
            });

        PlotOptions = await currentOwnedPlots
            .Union(currentlyLinkedPlots)
            .OrderBy(item => item.Text)
            .ToListAsync(cancellationToken);
    }
}
