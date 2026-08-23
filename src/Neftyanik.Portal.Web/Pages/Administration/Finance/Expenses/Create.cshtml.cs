using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFinancialAuditService _financialAuditService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateModel(ApplicationDbContext dbContext, IFinancialAuditService financialAuditService, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _financialAuditService = financialAuditService;
        _userManager = userManager;
    }

    [BindProperty]
    public ExpenseInputModel Input { get; set; } = new();

    public IReadOnlyList<SelectListItem> ExpenseCategoryOptions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadExpenseCategoryOptionsAsync(cancellationToken);
        Input.ExpenseDate = DateOnly.FromDateTime(DateTime.Today);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadExpenseCategoryOptionsAsync(cancellationToken);

        var validCategoryIds = ExpenseCategoryOptions.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
        if (!Input.ExpenseCategoryId.HasValue || !validCategoryIds.Contains(Input.ExpenseCategoryId.Value.ToString()))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(ExpenseInputModel.ExpenseCategoryId)}", "Выберите активный тип расхода.");
        }

        var currentUser = await _userManager.GetUserAsync(User);
        if (currentUser is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Input.ExpenseCategoryId is null || Input.ExpenseDate is null || Input.Amount is null)
        {
            return Page();
        }

        var expenseDate = Input.ExpenseDate.Value;
        var amount = Input.Amount.Value;

        var expense = new Expense
        {
            ExpenseCategoryId = Input.ExpenseCategoryId.Value,
            ExpenseDate = expenseDate,
            Amount = amount,
            Description = Input.Description.Trim(),
            Payee = Normalize(Input.Payee),
            DocumentNumber = Normalize(Input.DocumentNumber),
            CreatedByUserId = currentUser.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction is null)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            _dbContext.Expenses.Add(expense);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _financialAuditService.Add(
                FinancialAuditLogActions.Created,
                nameof(Expense),
                expense.Id.ToString(),
                $"Создан расход #{expense.Id}.",
                newValues: new
                {
                    ExpenseId = expense.Id,
                    expense.ExpenseDate,
                    expense.Amount,
                    expense.ExpenseCategoryId,
                    expense.Description,
                    expense.Payee,
                    expense.DocumentNumber,
                    expense.AssociationElectricityReadingId
                });

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        TempData["SuccessMessage"] = "Расход сохранён.";
        return RedirectToPage("/Administration/Finance/Expenses/Index");
    }

    private async Task LoadExpenseCategoryOptionsAsync(CancellationToken cancellationToken)
    {
        ExpenseCategoryOptions = await _dbContext.ExpenseCategories
            .AsNoTracking()
            .Where(category => category.IsActive && category.Id != Neftyanik.Portal.Domain.Constants.ExpenseCategoryIds.ElectricityPayment)
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

}
