using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ExpenseCategories;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class ArchiveModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public ArchiveModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public ExpenseCategoryArchiveViewModel ExpenseCategory { get; private set; } = new();

    public bool IsArchiveOperation => ExpenseCategory.IsActive;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var expenseCategory = await LoadViewModelAsync(id, cancellationToken);
        if (expenseCategory is null)
        {
            return NotFound();
        }

        ExpenseCategory = expenseCategory;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        var expenseCategory = await _dbContext.ExpenseCategories.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (expenseCategory is null)
        {
            return NotFound();
        }

        if (expenseCategory.Id == ExpenseCategoryIds.ElectricityPayment && expenseCategory.IsActive)
        {
            TempData["ErrorMessage"] = "Системный тип расхода 'Электроэнергия' нельзя перевести в архив.";
            return RedirectToPage("/Administration/Finance/ExpenseCategories/Index");
        }

        var willArchive = expenseCategory.IsActive;
        expenseCategory.IsActive = !expenseCategory.IsActive;

        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = willArchive
            ? "Тип расхода переведен в архив."
            : "Тип расхода восстановлен из архива.";

        return RedirectToPage("/Administration/Finance/ExpenseCategories/Index");
    }

    private async Task<ExpenseCategoryArchiveViewModel?> LoadViewModelAsync(int id, CancellationToken cancellationToken)
    {
        return await _dbContext.ExpenseCategories
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new ExpenseCategoryArchiveViewModel
            {
                Id = item.Id,
                Name = item.Name,
                IsActive = item.IsActive,
                ExpensesCount = item.Expenses.Count(),
                IsElectricityCategory = item.Id == ExpenseCategoryIds.ElectricityPayment
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public sealed class ExpenseCategoryArchiveViewModel
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public bool IsActive { get; init; }

        public int ExpensesCount { get; init; }

        public bool IsElectricityCategory { get; init; }
    }
}
