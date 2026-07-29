using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class IndexModel : PageModel
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "active";

    [BindProperty(SupportsGet = true)]
    public string Ownership { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty]
    public PlotChargeInputModel ChargeInput { get; set; } = new();

    public IReadOnlyList<PlotListItem> Plots { get; private set; } = [];

    public IReadOnlyList<SelectListItem> ChargeTypeOptions { get; private set; } = [];

    public IReadOnlyDictionary<int, decimal?> ChargeTypeDefaultAmounts { get; private set; } = new Dictionary<int, decimal?>();

    private IReadOnlyDictionary<int, ChargeTypeRuleViewModel> ChargeTypeRules { get; set; } = new Dictionary<int, ChargeTypeRuleViewModel>();

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; }

    public string EmptyStateMessage { get; private set; } = string.Empty;

    public bool HasSingleChargeType => ChargeTypeOptions.Count == 1;

    public string SingleChargeTypeText => HasSingleChargeType ? ChargeTypeOptions[0].Text : string.Empty;

    public bool ReopenChargeModal { get; private set; }

    public string CurrentChargeDateDisplay => DateOnly.FromDateTime(DateTime.Today).ToString("dd.MM.yyyy");

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        NormalizeFilterState();
        await LoadPlotsAsync(cancellationToken);
        await LoadChargeTypeOptionsAsync(cancellationToken);

        ChargeInput.ChargeDate ??= DateOnly.FromDateTime(DateTime.Today);
        ChargeInput.ChargeTypeId ??= GetDefaultChargeTypeId();
        ChargeInput.Amount ??= GetChargeTypeDefaultAmount(ChargeInput.ChargeTypeId);
    }

    public async Task<IActionResult> OnPostCreateChargesAsync(CancellationToken cancellationToken)
    {
        NormalizeFilterState();
        ChargeInput.SelectedPlotIds = ChargeInput.SelectedPlotIds.Distinct().ToList();

        await LoadPlotsAsync(cancellationToken);
        await LoadChargeTypeOptionsAsync(cancellationToken);

        var currentDate = DateOnly.FromDateTime(DateTime.Now);
        var selectedPlots = ChargeInput.SelectedPlotIds.Count == 0
            ? []
            : await _dbContext.Plots
                .AsNoTracking()
                .Where(plot => ChargeInput.SelectedPlotIds.Contains(plot.Id))
                .Select(plot => new SelectedPlotChargeCandidate
                {
                    PlotId = plot.Id,
                    PlotNumber = plot.Number,
                    IsEligible = plot.IsActive && plot.PlotOwnerships.Any(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                        && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate))
                })
                .ToListAsync(cancellationToken);

        var validSelectedPlotIds = selectedPlots
            .Where(plot => plot.IsEligible)
            .Select(plot => plot.PlotId)
            .ToList();

        if (ChargeInput.SelectedPlotIds.Count == 0)
        {
            ModelState.AddModelError(nameof(ChargeInput.SelectedPlotIds), "Выберите хотя бы один участок.");
        }

        if (ChargeTypeOptions.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Нет доступных активных типов начислений.");
        }

        if (!ChargeInput.ChargeTypeId.HasValue && ChargeTypeOptions.Count > 0)
        {
            ChargeInput.ChargeTypeId = int.Parse(ChargeTypeOptions[0].Value);
        }

        var validChargeTypeIds = ChargeTypeOptions.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
        if (ChargeInput.ChargeTypeId.HasValue && !validChargeTypeIds.Contains(ChargeInput.ChargeTypeId.Value.ToString()))
        {
            ModelState.AddModelError(nameof(ChargeInput.ChargeTypeId), "Выберите активный тип начисления.");
        }

        var chargeTypeDefaultAmount = GetChargeTypeDefaultAmount(ChargeInput.ChargeTypeId);
        if (!chargeTypeDefaultAmount.HasValue || chargeTypeDefaultAmount.Value <= 0m)
        {
            ModelState.AddModelError(nameof(ChargeInput.ChargeTypeId), "Для выбранного типа начисления не задана корректная сумма по умолчанию.");
        }

        var chargeTypeRules = ChargeInput.ChargeTypeId.HasValue ? GetChargeTypeRules(ChargeInput.ChargeTypeId.Value) : null;
        if (ChargeInput.ChargeTypeId.HasValue && chargeTypeRules is null)
        {
            ModelState.AddModelError(nameof(ChargeInput.ChargeTypeId), "Выберите активный тип начисления.");
        }

        ChargeInput.Amount = chargeTypeDefaultAmount;
        ChargeInput.ChargeDate = DateOnly.FromDateTime(DateTime.Today);

        if (ChargeInput.DueDate.HasValue && ChargeInput.DueDate.Value < ChargeInput.ChargeDate.Value)
        {
            ModelState.AddModelError(nameof(ChargeInput.DueDate), "Срок оплаты не может быть раньше даты начисления.");
        }

        List<string> duplicatePlotNumbers = [];
        if (validSelectedPlotIds.Count > 0 && ChargeInput.ChargeTypeId.HasValue && chargeTypeRules is not null)
        {
            if (chargeTypeRules.IsYearly)
            {
                var periodStart = new DateOnly(DateTime.Today.Year, 1, 1);
                var periodEnd = new DateOnly(DateTime.Today.Year, 12, 31);
                var duplicatePlots = await _dbContext.Charges
                    .AsNoTracking()
                    .Where(charge => charge.CancelledAtUtc == null
                        && charge.PlotId.HasValue
                        && validSelectedPlotIds.Contains(charge.PlotId.Value)
                        && charge.ChargeTypeId == ChargeInput.ChargeTypeId.Value
                        && charge.ChargeDate >= periodStart
                        && charge.ChargeDate <= periodEnd)
                    .Select(charge => new
                    {
                        PlotId = charge.PlotId!.Value,
                        PlotNumber = charge.Plot != null ? charge.Plot.Number : null
                    })
                    .ToListAsync(cancellationToken);

                duplicatePlotNumbers = duplicatePlots
                    .Select(item => string.IsNullOrWhiteSpace(item.PlotNumber) ? $"#{item.PlotId}" : item.PlotNumber)
                    .Distinct()
                    .OrderBy(number => number)
                    .ToList();
            }
            else if (chargeTypeRules.OnlyOnOwnerChange)
            {
                var ownershipStarts = await _dbContext.PlotOwnerships
                    .AsNoTracking()
                    .Where(ownership => validSelectedPlotIds.Contains(ownership.PlotId)
                        && (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= ChargeInput.ChargeDate!.Value)
                        && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= ChargeInput.ChargeDate.Value))
                    .Select(ownership => new
                    {
                        ownership.PlotId,
                        OwnershipStart = ownership.ValidFrom ?? DateOnly.MinValue,
                        ownership.ValidTo
                    })
                    .ToListAsync(cancellationToken);

                var duplicatePlotIds = new HashSet<int>();
                foreach (var ownership in ownershipStarts)
                {
                    var exists = await _dbContext.Charges
                        .AsNoTracking()
                        .AnyAsync(charge => charge.CancelledAtUtc == null
                            && charge.PlotId == ownership.PlotId
                            && charge.ChargeTypeId == ChargeInput.ChargeTypeId.Value
                            && charge.ChargeDate >= ownership.OwnershipStart
                            && (!ownership.ValidTo.HasValue || charge.ChargeDate <= ownership.ValidTo.Value),
                            cancellationToken);

                    if (exists)
                    {
                        duplicatePlotIds.Add(ownership.PlotId);
                    }
                }

                duplicatePlotNumbers = selectedPlots
                    .Where(plot => duplicatePlotIds.Contains(plot.PlotId))
                    .Select(plot => plot.PlotNumber)
                    .Distinct()
                    .OrderBy(number => number)
                    .ToList();
            }
        }

        if (!ModelState.IsValid)
        {
            ReopenChargeModal = true;
            return Page();
        }

        var ineligiblePlotNumbers = selectedPlots
            .Where(plot => !plot.IsEligible)
            .Select(plot => plot.PlotNumber)
            .OrderBy(number => number)
            .ToList();

        var chargeablePlotIds = validSelectedPlotIds
            .Where(plotId => !duplicatePlotNumbers.Contains(selectedPlots.First(plot => plot.PlotId == plotId).PlotNumber, StringComparer.Ordinal))
            .ToList();

        if (chargeablePlotIds.Count == 0)
        {
            if (TempData is not null)
            {
                if (duplicatePlotNumbers.Count > 0)
                {
                    TempData["ErrorMessage"] = chargeTypeRules?.OnlyOnOwnerChange == true
                        ? $"Не удалось создать начисления. Для текущего владельца уже есть начисления этого типа по участкам: {string.Join(", ", duplicatePlotNumbers)}."
                        : $"Не удалось создать начисления. Уже есть начисления в текущем году для участков: {string.Join(", ", duplicatePlotNumbers)}.";
                }
                else if (ineligiblePlotNumbers.Count > 0)
                {
                    TempData["ErrorMessage"] = $"Не удалось создать начисления. Недоступны для начисления участки: {string.Join(", ", ineligiblePlotNumbers)}.";
                }
            }

            return RedirectToPage("/Administration/Plots/Index", new
            {
                search = Search,
                status = Status,
                ownership = Ownership,
                pageNumber = PageNumber
            });
        }

        var currentUser = await _userManager.GetUserAsync(User);
        var normalizedDescription = Normalize(ChargeInput.Description);
        var charges = chargeablePlotIds
            .Select(plotId => new Charge
            {
                PlotId = plotId,
                ChargeTypeId = ChargeInput.ChargeTypeId!.Value,
                Amount = chargeTypeDefaultAmount!.Value,
                ChargeDate = ChargeInput.ChargeDate!.Value,
                DueDate = ChargeInput.DueDate,
                Description = normalizedDescription,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = currentUser?.Id
            })
            .ToList();

        _dbContext.Charges.AddRange(charges);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (TempData is not null)
        {
            var successPlotNumbers = selectedPlots
                .Where(plot => chargeablePlotIds.Contains(plot.PlotId))
                .Select(plot => plot.PlotNumber)
                .OrderBy(number => number)
                .ToList();

            TempData["SuccessMessage"] = charges.Count == 1
                ? $"Начисление создано для участка: {successPlotNumbers[0]}."
                : $"Успешно созданы начисления для участков: {string.Join(", ", successPlotNumbers)}.";

            var failureParts = new List<string>();
            if (duplicatePlotNumbers.Count > 0)
            {
                failureParts.Add(chargeTypeRules?.OnlyOnOwnerChange == true
                    ? $"для текущего владельца уже есть начисления этого типа: {string.Join(", ", duplicatePlotNumbers)}"
                    : $"уже есть начисления в текущем году: {string.Join(", ", duplicatePlotNumbers)}");
            }

            if (ineligiblePlotNumbers.Count > 0)
            {
                failureParts.Add($"недоступны для начисления: {string.Join(", ", ineligiblePlotNumbers)}");
            }

            if (failureParts.Count > 0)
            {
                TempData["ErrorMessage"] = $"Не созданы начисления для части участков: {string.Join("; ", failureParts)}.";
            }
        }

        return RedirectToPage("/Administration/Plots/Index", new
        {
            search = Search,
            status = Status,
            ownership = Ownership,
            pageNumber = PageNumber
        });
    }

    private async Task LoadPlotsAsync(CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);

        IQueryable<Plot> query = _dbContext.Plots.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(plot =>
                plot.Number.Contains(Search) ||
                (plot.Address != null && plot.Address.Contains(Search)) ||
                (plot.CadastralNumber != null && plot.CadastralNumber.Contains(Search)));
        }

        query = Status switch
        {
            "archived" => query.Where(plot => !plot.IsActive),
            "all" => query,
            _ => query.Where(plot => plot.IsActive)
        };

        query = Ownership switch
        {
            "withowners" => query.Where(plot => plot.PlotOwnerships.Any(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate))),
            "withoutowners" => query.Where(plot => !plot.PlotOwnerships.Any(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate))),
            _ => query
        };

        TotalCount = await query.CountAsync(cancellationToken);
        TotalPages = TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        Plots = await query
            .OrderBy(plot => plot.Number)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(plot => new PlotListItem
            {
                Id = plot.Id,
                Number = plot.Number,
                Address = plot.Address,
                AreaSquareMeters = plot.AreaSquareMeters,
                CadastralNumber = plot.CadastralNumber,
                IsActive = plot.IsActive,
                OwnersCount = plot.PlotOwnerships.Count(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                    && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate)),
                OwnerMemberId = plot.PlotOwnerships
                    .Where(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                        && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate)
                        && ownership.Member != null)
                    .OrderByDescending(ownership => ownership.IsPrimaryContact)
                    .ThenBy(ownership => ownership.Member!.FullName)
                    .Select(ownership => (int?)ownership.MemberId)
                    .FirstOrDefault(),
                OwnerFullName = plot.PlotOwnerships
                    .Where(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                        && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate)
                        && ownership.Member != null)
                    .OrderByDescending(ownership => ownership.IsPrimaryContact)
                    .ThenBy(ownership => ownership.Member!.FullName)
                    .Select(ownership => ownership.Member!.FullName)
                    .FirstOrDefault(),
                CanCreateCharge = plot.IsActive && plot.PlotOwnerships.Any(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                    && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate))
            })
            .ToListAsync(cancellationToken);

        EmptyStateMessage = TotalCount == 0 && string.IsNullOrWhiteSpace(Search) && Status == "all"
            ? "Участки пока не добавлены."
            : "По выбранным условиям участки не найдены.";
    }

    private async Task LoadChargeTypeOptionsAsync(CancellationToken cancellationToken)
    {
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
            .Select(item => new SelectListItem
            {
                Value = item.Id.ToString(),
                Text = BuildChargeTypeDisplayText(item)
            })
            .ToList();

        ChargeTypeDefaultAmounts = chargeTypes.ToDictionary(item => item.Id, item => item.DefaultAmount);
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

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public sealed class PlotListItem
    {
        public int Id { get; init; }

        public string Number { get; init; } = string.Empty;

        public string? Address { get; init; }

        public decimal? AreaSquareMeters { get; init; }

        public string? CadastralNumber { get; init; }

        public bool IsActive { get; init; }

        public int OwnersCount { get; init; }

        public int? OwnerMemberId { get; init; }

        public string? OwnerFullName { get; init; }

        public bool CanCreateCharge { get; init; }
    }

    private sealed class SelectedPlotChargeCandidate
    {
        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public bool IsEligible { get; init; }
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

    private void NormalizeFilterState()
    {
        Status = NormalizeStatus(Status);
        Ownership = NormalizeOwnership(Ownership);
        PageNumber = PageNumber < 1 ? 1 : PageNumber;
        Search = Normalize(Search);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private decimal? GetChargeTypeDefaultAmount(int? chargeTypeId)
    {
        if (!chargeTypeId.HasValue)
        {
            return null;
        }

        return ChargeTypeDefaultAmounts.TryGetValue(chargeTypeId.Value, out var amount)
            ? amount
            : null;
    }
}
