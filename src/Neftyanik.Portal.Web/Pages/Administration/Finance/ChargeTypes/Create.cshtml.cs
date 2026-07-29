using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ChargeTypes;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public CreateModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public ChargeTypeInputModel Input { get; set; } = new();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (await NameExistsAsync(null, cancellationToken))
        {
            ModelState.AddModelError("Input.Name", "Тип начисления с таким наименованием уже существует.");
        }

        if (await HasAnotherDefaultAsync(null, cancellationToken))
        {
            ModelState.AddModelError("Input.IsDefault", "Тип начисления по умолчанию уже выбран. Снимите признак у другого активного типа начисления.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var chargeType = new ChargeType
        {
            Name = Input.Name.Trim(),
            Description = Normalize(Input.Description),
            DefaultAmount = Input.DefaultAmount,
            IsDefault = Input.IsDefault,
            IsYearly = Input.IsYearly,
            OnlyOnOwnerChange = Input.OnlyOnOwnerChange,
            IsActive = Input.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.ChargeTypes.Add(chargeType);
        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = "Тип начисления успешно создан.";
        return RedirectToPage("/Administration/Finance/ChargeTypes/Index");
    }

    private async Task<bool> NameExistsAsync(int? currentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            return false;
        }

        var normalizedName = Input.Name.Trim();
        return await _dbContext.ChargeTypes
            .AsNoTracking()
            .AnyAsync(chargeType => chargeType.Name == normalizedName && (!currentId.HasValue || chargeType.Id != currentId.Value), cancellationToken);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task<bool> HasAnotherDefaultAsync(int? currentId, CancellationToken cancellationToken)
    {
        if (!Input.IsDefault || !Input.IsActive)
        {
            return false;
        }

        return await _dbContext.ChargeTypes
            .AsNoTracking()
            .AnyAsync(chargeType => chargeType.IsDefault
                && chargeType.IsActive
                && (!currentId.HasValue || chargeType.Id != currentId.Value), cancellationToken);
    }
}
