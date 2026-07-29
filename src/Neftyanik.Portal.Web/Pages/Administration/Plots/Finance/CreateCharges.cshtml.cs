using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;

[Microsoft.AspNetCore.Authorization.Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CreateChargesModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateChargesModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [BindProperty]
    public PlotChargeInputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "active";

    [BindProperty(SupportsGet = true)]
    public string Ownership { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<SelectedPlotViewModel> SelectedPlots { get; private set; } = [];

    public IReadOnlyList<SelectListItem> ChargeTypeOptions { get; private set; } = [];

    private IReadOnlyDictionary<int, ChargeTypeRuleViewModel> ChargeTypeRules { get; set; } = new Dictionary<int, ChargeTypeRuleViewModel>();

    public bool HasSingleChargeType => ChargeTypeOptions.Count == 1;

    public string SingleChargeTypeText => HasSingleChargeType ? ChargeTypeOptions[0].Text : string.Empty;

    public async Task<IActionResult> OnGetAsync([FromQuery] List<int> selectedPlotIds, CancellationToken cancellationToken)
    {
        NormalizeReturnState();
        Input.SelectedPlotIds = selectedPlotIds.Distinct().ToList();

        await LoadPageStateAsync(cancellationToken);

        if (Input.SelectedPlotIds.Count == 0)
        {
            TempData["ErrorMessage"] = "Выберите хотя бы один участок для начисления.";
            return RedirectToPlotsIndex();
        }

        if (SelectedPlots.Count != Input.SelectedPlotIds.Count)
        {
            TempData["ErrorMessage"] = "Для начисления доступны только активные участки с текущими владельцами.";
            return RedirectToPlotsIndex();
        }

        if (ChargeTypeOptions.Count == 0)
        {
            TempData["ErrorMessage"] = "Сначала создайте активный тип начисления.";
            return RedirectToPlotsIndex();
        }

        Input.ChargeTypeId = GetDefaultChargeTypeId();
        Input.ChargeDate = DateOnly.FromDateTime(DateTime.Today);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        NormalizeReturnState();
        Input.SelectedPlotIds = Input.SelectedPlotIds.Distinct().ToList();

        await LoadPageStateAsync(cancellationToken);

        if (ChargeTypeOptions.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Нет доступных активных типов начислений.");
        }

        if (!Input.ChargeTypeId.HasValue && ChargeTypeOptions.Count > 0)
        {
            Input.ChargeTypeId = int.Parse(ChargeTypeOptions[0].Value);
        }

        if (SelectedPlots.Count != Input.SelectedPlotIds.Count)
        {
            ModelState.AddModelError(nameof(Input.SelectedPlotIds), "Для начисления доступны только активные участки с текущими владельцами.");
        }

        var validChargeTypeIds = ChargeTypeOptions.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
        if (Input.ChargeTypeId.HasValue && !validChargeTypeIds.Contains(Input.ChargeTypeId.Value.ToString()))
        {
            ModelState.AddModelError(nameof(Input.ChargeTypeId), "Выберите активный тип начисления.");
        }

        var chargeTypeRules = Input.ChargeTypeId.HasValue ? GetChargeTypeRules(Input.ChargeTypeId.Value) : null;
        if (Input.ChargeTypeId.HasValue && chargeTypeRules is null)
        {
            ModelState.AddModelError(nameof(Input.ChargeTypeId), "Выберите активный тип начисления.");
        }

        var chargeDate = Input.ChargeDate!.Value;

        List<int> duplicatePlotIds = [];
        if (SelectedPlots.Count > 0 && Input.ChargeTypeId.HasValue && chargeTypeRules is not null)
        {
            if (chargeTypeRules.IsYearly)
            {
                var periodStart = new DateOnly(chargeDate.Year, 1, 1);
                var periodEnd = new DateOnly(chargeDate.Year, 12, 31);
                duplicatePlotIds = await _dbContext.Charges
                    .AsNoTracking()
                    .Where(charge => charge.CancelledAtUtc == null
                        && charge.PlotId.HasValue
                        && Input.SelectedPlotIds.Contains(charge.PlotId.Value)
                        && charge.ChargeTypeId == Input.ChargeTypeId.Value
                        && charge.ChargeDate >= periodStart
                        && charge.ChargeDate <= periodEnd)
                    .Select(charge => charge.PlotId!.Value)
                    .Distinct()
                    .ToListAsync(cancellationToken);
            }
            else if (chargeTypeRules.OnlyOnOwnerChange)
            {
                var ownerships = await _dbContext.PlotOwnerships
                    .AsNoTracking()
                    .Where(ownership => Input.SelectedPlotIds.Contains(ownership.PlotId)
                        && (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= chargeDate)
                        && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= chargeDate))
                    .Select(ownership => new
                    {
                        ownership.PlotId,
                        OwnershipStart = ownership.ValidFrom ?? DateOnly.MinValue,
                        ownership.ValidTo
                    })
                    .ToListAsync(cancellationToken);

                foreach (var ownership in ownerships)
                {
                    var exists = await _dbContext.Charges
                        .AsNoTracking()
                        .AnyAsync(charge => charge.CancelledAtUtc == null
                            && charge.PlotId == ownership.PlotId
                            && charge.ChargeTypeId == Input.ChargeTypeId.Value
                            && charge.ChargeDate >= ownership.OwnershipStart
                            && (!ownership.ValidTo.HasValue || charge.ChargeDate <= ownership.ValidTo.Value),
                            cancellationToken);

                    if (exists)
                    {
                        duplicatePlotIds.Add(ownership.PlotId);
                    }
                }
            }
        }

        if (duplicatePlotIds.Count > 0)
        {
            var duplicatePlotNumbers = SelectedPlots
                .Where(plot => duplicatePlotIds.Contains(plot.Id))
                .Select(plot => plot.Number)
                .Distinct()
                .OrderBy(number => number)
                .ToList();

            ModelState.AddModelError(
                string.Empty,
                chargeTypeRules?.OnlyOnOwnerChange == true
                    ? $"Для текущего владельца уже есть начисление этого типа по участкам: {string.Join(", ", duplicatePlotNumbers)}."
                    : $"За указанный год уже существуют ежегодные начисления этого типа по участкам: {string.Join(", ", duplicatePlotNumbers)}.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var normalizedDescription = Normalize(Input.Description);
        var charges = Input.SelectedPlotIds
            .Select(plotId => new Charge
            {
                PlotId = plotId,
                ChargeTypeId = Input.ChargeTypeId!.Value,
                Amount = Input.Amount!.Value,
                ChargeDate = Input.ChargeDate!.Value,
                DueDate = Input.DueDate,
                Description = normalizedDescription,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = currentUser?.Id
            })
            .ToList();

        _dbContext.Charges.AddRange(charges);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (TempData is not null)
        {
            TempData["SuccessMessage"] = charges.Count == 1
                ? "Начисление сохранено."
                : $"Создано начислений: {charges.Count}.";
        }

        return RedirectToPlotsIndex();
    }

    private async Task LoadPageStateAsync(CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);

        SelectedPlots = Input.SelectedPlotIds.Count == 0
            ? []
            : await _dbContext.Plots
                .AsNoTracking()
                .Where(plot => Input.SelectedPlotIds.Contains(plot.Id)
                    && plot.IsActive
                    && plot.PlotOwnerships.Any(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                        && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate)))
                .OrderBy(plot => plot.Number)
                .Select(plot => new SelectedPlotViewModel
                {
                    Id = plot.Id,
                    Number = plot.Number,
                    Address = plot.Address,
                    OwnersCount = plot.PlotOwnerships.Count(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                        && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate))
                })
                .ToListAsync(cancellationToken);

        var chargeTypes = await _dbContext.ChargeTypes
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.Name)
            .Select(item => new ChargeTypeRuleViewModel
            {
                Id = item.Id,
                Name = item.Name,
                DefaultAmount = item.DefaultAmount,
                IsDefault = item.IsDefault,
                IsYearly = item.IsYearly,
                OnlyOnOwnerChange = item.OnlyOnOwnerChange
            })
            .ToListAsync(cancellationToken);

        ChargeTypeRules = chargeTypes.ToDictionary(item => item.Id);

        ChargeTypeOptions = chargeTypes
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.Name)
            .Select(item => new SelectListItem
            {
                Value = item.Id.ToString(),
                Text = BuildChargeTypeDisplayText(item)
            })
            .ToList();
    }

    private int? GetDefaultChargeTypeId()
    {
        var defaultChargeType = ChargeTypeRules.Values.FirstOrDefault(item => item.IsDefault);
        if (defaultChargeType is not null)
        {
            return defaultChargeType.Id;
        }

        return ChargeTypeOptions.Count > 0 ? int.Parse(ChargeTypeOptions[0].Value) : null;
    }

    private ChargeTypeRuleViewModel? GetChargeTypeRules(int chargeTypeId)
    {
        return ChargeTypeRules.TryGetValue(chargeTypeId, out var chargeType) ? chargeType : null;
    }

    private static string BuildChargeTypeDisplayText(ChargeTypeRuleViewModel chargeType)
    {
        var suffixes = new List<string>();

        if (chargeType.DefaultAmount.HasValue)
        {
            suffixes.Add($"{chargeType.DefaultAmount.Value:0.00} грн по умолчанию");
        }

        if (chargeType.IsDefault)
        {
            suffixes.Add("по умолчанию");
        }

        if (chargeType.IsYearly)
        {
            suffixes.Add("ежегодный");
        }

        if (chargeType.OnlyOnOwnerChange)
        {
            suffixes.Add("при смене владельца");
        }

        return suffixes.Count == 0
            ? chargeType.Name
            : $"{chargeType.Name} ({string.Join(", ", suffixes)})";
    }

    private RedirectToPageResult RedirectToPlotsIndex()
    {
        return RedirectToPage("/Administration/Plots/Index", new
        {
            search = Search,
            status = Status,
            ownership = Ownership,
            pageNumber = PageNumber
        });
    }

    private void NormalizeReturnState()
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
        Status = NormalizeStatus(Status);
        Ownership = NormalizeOwnership(Ownership);
        PageNumber = PageNumber < 1 ? 1 : PageNumber;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "all" => "all",
            "archived" => "archived",
            _ => "active"
        };
    }

    private static string NormalizeOwnership(string? ownership)
    {
        return ownership?.ToLowerInvariant() switch
        {
            "withowners" => "withowners",
            "withoutowners" => "withoutowners",
            _ => "all"
        };
    }

    public sealed class SelectedPlotViewModel
    {
        public int Id { get; init; }

        public string Number { get; init; } = string.Empty;

        public string? Address { get; init; }

        public int OwnersCount { get; init; }

        public string? PrimaryContact { get; init; }
    }

    private sealed class ChargeTypeRuleViewModel
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public decimal? DefaultAmount { get; init; }

        public bool IsDefault { get; init; }

        public bool IsYearly { get; init; }

        public bool OnlyOnOwnerChange { get; init; }
    }
}
