using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ChargeTypes;

[Authorize(Roles = RoleNames.Administrator)]
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
            IsActive = chargeType.IsActive
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        ChargeTypeId = id;

        if (await NameExistsAsync(id, cancellationToken))
        {
            ModelState.AddModelError("Input.Name", "Тип начисления с таким наименованием уже существует.");
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
        chargeType.IsActive = Input.IsActive;
        chargeType.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = "Изменения по типу начисления сохранены.";
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
}
