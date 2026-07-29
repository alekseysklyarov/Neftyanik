using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ExpenseCategories;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public EditModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public ExpenseCategoryInputModel Input { get; set; } = new();

    public int ExpenseCategoryId { get; private set; }

    public int ExpensesCount { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var expenseCategory = await _dbContext.ExpenseCategories
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Description,
                item.IsActive,
                ExpensesCount = item.Expenses.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (expenseCategory is null)
        {
            return NotFound();
        }

        ExpenseCategoryId = expenseCategory.Id;
        ExpensesCount = expenseCategory.ExpensesCount;
        Input = new ExpenseCategoryInputModel
        {
            Name = expenseCategory.Name,
            Description = expenseCategory.Description,
            IsActive = expenseCategory.IsActive
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        ExpenseCategoryId = id;

        var expensesCount = await _dbContext.Expenses
            .AsNoTracking()
            .CountAsync(item => item.ExpenseCategoryId == id, cancellationToken);
        ExpensesCount = expensesCount;

        if (await NameExistsAsync(id, cancellationToken))
        {
            ModelState.AddModelError("Input.Name", AppLocalizer.Get("Тип расхода с таким наименованием уже существует.", "Тип витрати з такою назвою вже існує.", "An expense type with this name already exists."));
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var expenseCategory = await _dbContext.ExpenseCategories.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (expenseCategory is null)
        {
            return NotFound();
        }

        expenseCategory.Name = Input.Name.Trim();
        expenseCategory.Description = Normalize(Input.Description);
        expenseCategory.IsActive = Input.IsActive;

        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = AppLocalizer.Get("Изменения по типу расхода сохранены.", "Зміни типу витрати збережено.", "Expense type changes have been saved.");
        return RedirectToPage("/Administration/Finance/ExpenseCategories/Index");
    }

    private async Task<bool> NameExistsAsync(int currentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            return false;
        }

        var normalizedName = Input.Name.Trim();
        return await _dbContext.ExpenseCategories
            .AsNoTracking()
            .AnyAsync(category => category.Name == normalizedName && category.Id != currentId, cancellationToken);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
