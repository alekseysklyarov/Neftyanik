using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ExpenseCategories;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public CreateModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public ExpenseCategoryInputModel Input { get; set; } = new();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (await NameExistsAsync(null, cancellationToken))
        {
            ModelState.AddModelError("Input.Name", AppLocalizer.Get("Тип расхода с таким наименованием уже существует.", "Тип витрати з такою назвою вже існує.", "An expense type with this name already exists."));
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var expenseCategory = new ExpenseCategory
        {
            Name = Input.Name.Trim(),
            Description = Normalize(Input.Description),
            IsActive = Input.IsActive
        };

        _dbContext.ExpenseCategories.Add(expenseCategory);
        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = AppLocalizer.Get("Тип расхода успешно создан.", "Тип витрати успішно створено.", "The expense type has been created.");
        return RedirectToPage("/Administration/Finance/ExpenseCategories/Index");
    }

    private async Task<bool> NameExistsAsync(int? currentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            return false;
        }

        var normalizedName = Input.Name.Trim();
        return await _dbContext.ExpenseCategories
            .AsNoTracking()
            .AnyAsync(category => category.Name == normalizedName && (!currentId.HasValue || category.Id != currentId.Value), cancellationToken);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
