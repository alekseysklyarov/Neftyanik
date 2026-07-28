using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;

namespace Neftyanik.Portal.Web.Pages.Administration.Members.Finance;

[Authorize(Roles = RoleNames.Administrator)]
public class CreateChargeModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateChargeModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [BindProperty]
    public MemberChargeInputModel Input { get; set; } = new();

    public MemberSummaryViewModel Member { get; private set; } = new();

    public IReadOnlyList<SelectListItem> PlotOptions { get; private set; } = [];

    public IReadOnlyList<SelectListItem> ChargeTypeOptions { get; private set; } = [];

    private IReadOnlyDictionary<int, ChargeTypeRuleViewModel> ChargeTypeRules { get; set; } = new Dictionary<int, ChargeTypeRuleViewModel>();

    public bool HasSingleChargeType => ChargeTypeOptions.Count == 1;

    public string SingleChargeTypeText => HasSingleChargeType ? ChargeTypeOptions[0].Text : string.Empty;

    public async Task<IActionResult> OnGetAsync(int id, int? plotId, CancellationToken cancellationToken)
    {
        if (!await LoadPageStateAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (PlotOptions.Count == 0)
        {
            TempData["ErrorMessage"] = "У участника нет активных участков для начисления.";
            return RedirectToPage("/Administration/Members/Finance", new { id });
        }

        if (ChargeTypeOptions.Count == 0)
        {
            TempData["ErrorMessage"] = "Сначала создайте активный тип начисления.";
            return RedirectToPage("/Administration/Members/Finance", new { id });
        }

        Input.PlotId = plotId.HasValue && PlotOptions.Any(option => option.Value == plotId.Value.ToString())
            ? plotId.Value
            : PlotOptions.Count == 1 ? int.Parse(PlotOptions[0].Value) : null;
        Input.ChargeTypeId = GetDefaultChargeTypeId();
        Input.ChargeDate = DateOnly.FromDateTime(DateTime.Today);
        Input.Amount = GetChargeTypeDefaultAmount(Input.ChargeTypeId);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadPageStateAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (PlotOptions.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "У участника нет активных участков для начисления.");
        }

        if (ChargeTypeOptions.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Нет доступных активных типов начислений.");
        }

        if (!Input.ChargeTypeId.HasValue && ChargeTypeOptions.Count > 0)
        {
            Input.ChargeTypeId = int.Parse(ChargeTypeOptions[0].Value);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var validPlotIds = PlotOptions.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
        if (!validPlotIds.Contains(Input.PlotId!.Value.ToString()))
        {
            ModelState.AddModelError(nameof(Input.PlotId), "Выберите участок из списка текущих владений участника.");
            return Page();
        }

        var validChargeTypeIds = ChargeTypeOptions.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
        if (!validChargeTypeIds.Contains(Input.ChargeTypeId!.Value.ToString()))
        {
            ModelState.AddModelError(nameof(Input.ChargeTypeId), "Выберите активный тип начисления.");
            return Page();
        }

        var chargeTypeRules = GetChargeTypeRules(Input.ChargeTypeId.Value);
        if (chargeTypeRules is null)
        {
            ModelState.AddModelError(nameof(Input.ChargeTypeId), "Выберите активный тип начисления.");
            return Page();
        }

        var chargeDate = Input.ChargeDate!.Value;

        if (chargeTypeRules.IsYearly)
        {
            var periodStart = new DateOnly(chargeDate.Year, 1, 1);
            var periodEnd = new DateOnly(chargeDate.Year, 12, 31);
            var duplicateExists = await _dbContext.Charges
                .AsNoTracking()
                .AnyAsync(charge => charge.CancelledAtUtc == null
                    && charge.PlotId == Input.PlotId.Value
                    && charge.ChargeTypeId == Input.ChargeTypeId.Value
                    && charge.ChargeDate >= periodStart
                    && charge.ChargeDate <= periodEnd,
                    cancellationToken);

            if (duplicateExists)
            {
                ModelState.AddModelError(string.Empty, "Для этого участка уже существует ежегодное начисление выбранного типа за указанный год.");
                return Page();
            }
        }

        if (chargeTypeRules.OnlyOnOwnerChange)
        {
            var currentOwnership = await _dbContext.PlotOwnerships
                .AsNoTracking()
                .Where(ownership => ownership.PlotId == Input.PlotId.Value
                    && ownership.MemberId == id
                    && (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= chargeDate)
                    && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= chargeDate))
                .OrderByDescending(ownership => ownership.ValidFrom)
                .Select(ownership => new { ownership.ValidFrom, ownership.ValidTo })
                .FirstOrDefaultAsync(cancellationToken);

            if (currentOwnership is null)
            {
                ModelState.AddModelError(string.Empty, "Начисление этого типа можно создать только для участка с текущим владельцем на дату начисления.");
                return Page();
            }

            var ownershipStart = currentOwnership.ValidFrom ?? DateOnly.MinValue;
            var ownershipEnd = currentOwnership.ValidTo;

            var duplicateExists = await _dbContext.Charges
                .AsNoTracking()
                .AnyAsync(charge => charge.CancelledAtUtc == null
                    && charge.PlotId == Input.PlotId.Value
                    && charge.ChargeTypeId == Input.ChargeTypeId.Value
                    && charge.ChargeDate >= ownershipStart
                    && (!ownershipEnd.HasValue || charge.ChargeDate <= ownershipEnd.Value),
                    cancellationToken);

            if (duplicateExists)
            {
                ModelState.AddModelError(string.Empty, "Начисление этого типа уже создавалось для текущего владельца участка.");
                return Page();
            }
        }

        var currentUser = await _userManager.GetUserAsync(User);

        var charge = new Charge
        {
            PlotId = Input.PlotId.Value,
            ChargeTypeId = Input.ChargeTypeId.Value,
            Amount = Input.Amount!.Value,
            ChargeDate = Input.ChargeDate!.Value,
            DueDate = Input.DueDate,
            Description = Normalize(Input.Description),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = currentUser?.Id
        };

        _dbContext.Charges.Add(charge);
        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = "Начисление сохранено.";
        return RedirectToPage("/Administration/Members/Finance", new { id });
    }

    private async Task<bool> LoadPageStateAsync(int memberId, CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);

        var member = await _dbContext.Members
            .AsNoTracking()
            .Where(item => item.Id == memberId)
            .Select(item => new MemberSummaryViewModel
            {
                Id = item.Id,
                FullName = item.FullName,
                PhoneNumber = item.PhoneNumber,
                Email = item.Email,
                ActivePlotsCount = item.PlotOwnerships.Count(ownership => (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                    && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate))
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            return false;
        }

        Member = member;

        PlotOptions = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(memberId, currentDate)
            .OrderBy(ownership => ownership.Plot != null ? ownership.Plot.Number : string.Empty)
            .Select(ownership => new SelectListItem
            {
                Value = ownership.PlotId.ToString(),
                Text = ownership.Plot != null
                    ? $"Участок {ownership.Plot.Number}{(string.IsNullOrWhiteSpace(ownership.Plot.Address) ? string.Empty : $" — {ownership.Plot.Address}")}"
                    : $"Участок #{ownership.PlotId}"
            })
            .Distinct()
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

        return true;
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

    private decimal? GetChargeTypeDefaultAmount(int? chargeTypeId)
    {
        return chargeTypeId.HasValue && ChargeTypeRules.TryGetValue(chargeTypeId.Value, out var chargeType)
            ? chargeType.DefaultAmount
            : null;
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

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public sealed class MemberSummaryViewModel
    {
        public int Id { get; init; }

        public string FullName { get; init; } = string.Empty;

        public string? PhoneNumber { get; init; }

        public string? Email { get; init; }

        public int ActivePlotsCount { get; init; }
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
