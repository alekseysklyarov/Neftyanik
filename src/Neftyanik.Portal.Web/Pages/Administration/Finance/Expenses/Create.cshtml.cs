using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
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

        _dbContext.Expenses.Add(expense);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.Id,
            Action = "Create",
            EntityType = nameof(Expense),
            EntityId = expense.Id.ToString(),
            NewValues = JsonSerializer.Serialize(new
            {
                expense.ExpenseCategoryId,
                expense.ExpenseDate,
                expense.Amount,
                expense.Description,
                expense.Payee,
                expense.DocumentNumber
            }),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

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
