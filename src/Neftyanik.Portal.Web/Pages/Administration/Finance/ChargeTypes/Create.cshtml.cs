using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ChargeTypes;

[Authorize(Roles = RoleNames.Administrator)]
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

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var chargeType = new ChargeType
        {
            Name = Input.Name.Trim(),
            Description = Normalize(Input.Description),
            DefaultAmount = Input.DefaultAmount,
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
}
