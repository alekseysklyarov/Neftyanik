using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFinancialAuditService _financialAuditService;
    private readonly UserManager<ApplicationUser> _userManager;

    public EditModel(ApplicationDbContext dbContext, IFinancialAuditService financialAuditService, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _financialAuditService = financialAuditService;
        _userManager = userManager;
    }

    [BindProperty]
    public ExpenseInputModel Input { get; set; } = new();

    public long ExpenseId { get; private set; }

    public bool IsEditable { get; private set; }

    public IReadOnlyList<SelectListItem> ExpenseCategoryOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        await LoadExpenseCategoryOptionsAsync(cancellationToken);

        var expense = await _dbContext.Expenses
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.ExpenseCategoryId,
                item.ExpenseDate,
                item.Amount,
                item.Description,
                item.Payee,
                item.DocumentNumber,
                item.IsCancelled,
                item.AssociationElectricityReadingId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (expense is null)
        {
            return NotFound();
        }

        if (expense.AssociationElectricityReadingId.HasValue || expense.IsCancelled)
        {
            TempData["ErrorMessage"] = "Этот расход нельзя редактировать.";
            return RedirectToPage("/Administration/Finance/Expenses/Details", new { id });
        }

        ExpenseId = expense.Id;
        IsEditable = true;
        Input = new ExpenseInputModel
        {
            ExpenseCategoryId = expense.ExpenseCategoryId,
            ExpenseDate = expense.ExpenseDate,
            Amount = expense.Amount,
            Description = expense.Description,
            Payee = expense.Payee,
            DocumentNumber = expense.DocumentNumber
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long id, CancellationToken cancellationToken)
    {
        ExpenseId = id;
        await LoadExpenseCategoryOptionsAsync(cancellationToken);

        var validCategoryIds = ExpenseCategoryOptions.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
        if (!Input.ExpenseCategoryId.HasValue || !validCategoryIds.Contains(Input.ExpenseCategoryId.Value.ToString()))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(ExpenseInputModel.ExpenseCategoryId)}", "Выберите активный тип расхода.");
        }

        if (!ModelState.IsValid)
        {
            IsEditable = true;
            return Page();
        }

        var expense = await _dbContext.Expenses.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        if (Input.ExpenseCategoryId is null || Input.ExpenseDate is null || Input.Amount is null)
        {
            IsEditable = true;
            return Page();
        }

        if (expense.AssociationElectricityReadingId.HasValue || expense.IsCancelled)
        {
            TempData["ErrorMessage"] = "Этот расход нельзя редактировать.";
            return RedirectToPage("/Administration/Finance/Expenses/Details", new { id });
        }

        var oldValues = CreateAuditValues(expense);

        expense.ExpenseCategoryId = Input.ExpenseCategoryId.Value;
        expense.ExpenseDate = Input.ExpenseDate.Value;
        expense.Amount = Input.Amount.Value;
        expense.Description = Input.Description.Trim();
        expense.Payee = Normalize(Input.Payee);
        expense.DocumentNumber = Normalize(Input.DocumentNumber);
        expense.UpdatedAt = DateTimeOffset.UtcNow;

        var newValues = CreateAuditValues(expense);
        if (!AreEquivalent(oldValues, newValues))
        {
            _financialAuditService.Add(
                FinancialAuditLogActions.Updated,
                nameof(Expense),
                expense.Id.ToString(),
                $"Обновлен расход #{expense.Id}.",
                oldValues,
                newValues);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = "Изменения по расходу сохранены.";
        return RedirectToPage("/Administration/Finance/Expenses/Details", new { id });
    }

    private async Task LoadExpenseCategoryOptionsAsync(CancellationToken cancellationToken)
    {
        ExpenseCategoryOptions = await _dbContext.ExpenseCategories
            .AsNoTracking()
            .Where(category => category.IsActive && category.Id != ExpenseCategoryIds.ElectricityPayment)
            .OrderBy(category => category.Name)
            .Select(category => new SelectListItem
            {
                Value = category.Id.ToString(),
                Text = category.Name
            })
            .ToListAsync(cancellationToken);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ExpenseAuditValues CreateAuditValues(Expense expense)
    {
        return new ExpenseAuditValues(
            expense.Id,
            expense.ExpenseDate,
            expense.Amount,
            expense.ExpenseCategoryId,
            expense.Description,
            expense.Payee,
            expense.DocumentNumber,
            expense.AssociationElectricityReadingId,
            expense.IsCancelled,
            expense.CancellationReason,
            expense.CancelledAt);
    }

    private static bool AreEquivalent(ExpenseAuditValues oldValues, ExpenseAuditValues newValues)
    {
        return oldValues == newValues;
    }

    private sealed record ExpenseAuditValues(
        long ExpenseId,
        DateOnly ExpenseDate,
        decimal Amount,
        int ExpenseCategoryId,
        string Description,
        string? Payee,
        string? DocumentNumber,
        long? AssociationElectricityReadingId,
        bool IsCancelled,
        string? CancellationReason,
        DateTimeOffset? CancelledAt);
}
