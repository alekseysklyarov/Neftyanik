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

    [BindProperty(SupportsGet = true)]
    public int? SelectedMemberId { get; set; }

    [BindProperty]
    public MeterInputModel Input { get; set; } = new();

    public IReadOnlyList<SelectListItem> MemberOptions { get; private set; } = [];

    public IReadOnlyList<SelectListItem> PlotOptions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (SelectedMemberId.HasValue && !Input.MemberId.HasValue)
        {
            Input.MemberId = SelectedMemberId.Value;
        }

        await LoadOptionsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadOptionsAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var result = await _memberElectricityService.CreateMeterAsync(
            new CreateMemberElectricityMeterRequest(
                Input.MemberId!.Value,
                Input.MeterNumber,
                Input.Name,
                Input.IsActive,
                Input.BillingPlotId!.Value,
                Input.PlotIds,
                currentUser?.Id),
            cancellationToken);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Не удалось создать счётчик.");
            return Page();
        }

        TempData["SuccessMessage"] = "Счётчик участника создан.";
        return RedirectToPage("/Administration/Electricity/Meters/Details", new { id = result.MeterId });
    }

    private async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        MemberOptions = await _dbContext.Members
            .AsNoTracking()
            .Where(member => member.IsActive)
            .OrderBy(member => member.FullName)
            .Select(member => new SelectListItem
            {
                Value = member.Id.ToString(),
                Text = member.FullName
            })
            .ToListAsync(cancellationToken);

        var memberId = Input.MemberId ?? SelectedMemberId;
        if (!memberId.HasValue)
        {
            PlotOptions = [];
            return;
        }

        var currentDate = DateOnly.FromDateTime(DateTime.Today);
        PlotOptions = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(memberId.Value, currentDate)
            .OrderBy(ownership => ownership.Plot != null ? ownership.Plot.Number : string.Empty)
            .Select(ownership => new SelectListItem
            {
                Value = ownership.PlotId.ToString(),
                Text = ownership.Plot != null ? $"{ownership.Plot.Number} — {ownership.Plot.Address}" : ownership.PlotId.ToString()
            })
            .ToListAsync(cancellationToken);
    }
}
