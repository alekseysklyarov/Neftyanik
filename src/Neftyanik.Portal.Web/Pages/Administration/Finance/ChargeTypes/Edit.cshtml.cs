using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ChargeTypes;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public EditModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public ChargeTypeInputModel Input { get; set; } = new();

    public int ChargeTypeId { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var chargeType = await _dbContext.ChargeTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (chargeType is null)
        {
            return NotFound();
        }

        ChargeTypeId = chargeType.Id;
        Input = new ChargeTypeInputModel
        {
            Name = chargeType.Name,
            Description = chargeType.Description,
            DefaultAmount = chargeType.DefaultAmount,
            IsDefault = chargeType.IsDefault,
            IsYearly = chargeType.IsYearly,
            OnlyOnOwnerChange = chargeType.OnlyOnOwnerChange,
            IsActive = chargeType.IsActive
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        ChargeTypeId = id;

        if (await NameExistsAsync(id, cancellationToken))
        {
            ModelState.AddModelError("Input.Name", AppLocalizer.Get("Тип начисления с таким наименованием уже существует.", "Тип нарахування з такою назвою вже існує.", "A charge type with this name already exists."));
        }

        if (await HasAnotherDefaultAsync(id, cancellationToken))
        {
            ModelState.AddModelError("Input.IsDefault", AppLocalizer.Get("Тип начисления по умолчанию уже выбран. Снимите признак у другого активного типа начисления.", "Тип нарахування за замовчуванням уже вибрано. Зніміть ознаку з іншого активного типу нарахування.", "A default charge type has already been selected. Remove the flag from another active charge type."));
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var chargeType = await _dbContext.ChargeTypes.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (chargeType is null)
        {
            return NotFound();
        }

        chargeType.Name = Input.Name.Trim();
        chargeType.Description = Normalize(Input.Description);
        chargeType.DefaultAmount = Input.DefaultAmount;
        chargeType.IsDefault = Input.IsDefault;
        chargeType.IsYearly = Input.IsYearly;
        chargeType.OnlyOnOwnerChange = Input.OnlyOnOwnerChange;
        chargeType.IsActive = Input.IsActive;
        chargeType.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = AppLocalizer.Get("Изменения по типу начисления сохранены.", "Зміни типу нарахування збережено.", "Charge type changes have been saved.");
        return RedirectToPage("/Administration/Finance/ChargeTypes/Details", new { id });
    }

    private async Task<bool> NameExistsAsync(int currentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            return false;
        }

        var normalizedName = Input.Name.Trim();
        return await _dbContext.ChargeTypes
            .AsNoTracking()
            .AnyAsync(chargeType => chargeType.Name == normalizedName && chargeType.Id != currentId, cancellationToken);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task<bool> HasAnotherDefaultAsync(int currentId, CancellationToken cancellationToken)
    {
        if (!Input.IsDefault || !Input.IsActive)
        {
            return false;
        }

        return await _dbContext.ChargeTypes
            .AsNoTracking()
            .AnyAsync(chargeType => chargeType.IsDefault && chargeType.IsActive && chargeType.Id != currentId, cancellationToken);
    }
}
