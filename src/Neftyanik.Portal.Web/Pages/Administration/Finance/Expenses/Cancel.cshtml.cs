using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CancelModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFinancialAuditService _financialAuditService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CancelModel(ApplicationDbContext dbContext, IFinancialAuditService financialAuditService, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _financialAuditService = financialAuditService;
        _userManager = userManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public ExpenseCancelViewModel Expense { get; private set; } = new();

    public bool IsCancelOperation => !Expense.IsCancelled;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var expense = await LoadViewModelAsync(id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        Expense = expense;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long id, CancellationToken cancellationToken)
    {
        await using var transaction = _dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction is null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        await Neftyanik.Portal.Infrastructure.Services.AdvancePaymentAllocator.LockAsync(_dbContext, cancellationToken);
        var expense = await _dbContext.Expenses.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        if (!expense.IsCancelled && string.IsNullOrWhiteSpace(Input.CancellationReason))
        {
            var viewModel = await LoadViewModelAsync(id, cancellationToken);
            if (viewModel is null)
            {
                return NotFound();
            }

            Expense = viewModel;
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.CancellationReason)}", "Укажите причину отмены расхода.");
            return Page();
        }

        if (expense.IsCancelled && expense.AssociationElectricityReadingId.HasValue)
        {
            var total = await _dbContext.AssociationElectricityReadings
                .Where(r => r.Id == expense.AssociationElectricityReadingId).Select(r => r.TotalSupplierAmount).SingleAsync(cancellationToken);
            var paid = await _dbContext.Expenses.Where(e => e.AssociationElectricityReadingId == expense.AssociationElectricityReadingId && !e.IsCancelled)
                .Select(e => e.Amount).ToListAsync(cancellationToken);
            if (expense.Amount + paid.Sum() > (total ?? 0m))
            {
                Expense = (await LoadViewModelAsync(id, cancellationToken))!;
                ModelState.AddModelError(string.Empty, "Восстановление превысит сумму начисления поставщика. Сначала отмените заменяющую оплату.");
                return Page();
            }
        }
        var isCancelling = !expense.IsCancelled;
        var oldValues = CreateAuditValues(expense);

        expense.IsCancelled = isCancelling;
        expense.CancellationReason = isCancelling ? Input.CancellationReason?.Trim() : null;
        expense.CancelledAt = isCancelling ? DateTimeOffset.UtcNow : null;
        expense.UpdatedAt = DateTimeOffset.UtcNow;
        var newValues = CreateAuditValues(expense);

        if (isCancelling)
        {
            _financialAuditService.Add(
                FinancialAuditLogActions.Cancelled,
                nameof(Expense),
                expense.Id.ToString(),
                $"Отменен расход #{expense.Id}.",
                oldValues,
                newValues);
        }
        else
        {
            _financialAuditService.Add(
                FinancialAuditLogActions.Restored,
                nameof(Expense),
                expense.Id.ToString(),
                $"Восстановлен расход #{expense.Id}.",
                oldValues,
                newValues);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        TempData["SuccessMessage"] = expense.IsCancelled
            ? "Расход отменён."
            : "Расход восстановлен.";

        return RedirectToPage("/Administration/Finance/Expenses/Details", new { id });
    }

    private static object CreateAuditValues(Expense expense)
    {
        return new
        {
            ExpenseId = expense.Id,
            expense.ExpenseCategoryId,
            expense.ExpenseDate,
            expense.Amount,
            expense.Description,
            expense.Payee,
            expense.DocumentNumber,
            expense.IsCancelled,
            expense.CancellationReason,
            expense.CancelledAt,
            expense.AssociationElectricityReadingId
        };
    }

    private async Task<ExpenseCancelViewModel?> LoadViewModelAsync(long id, CancellationToken cancellationToken)
    {
        return await _dbContext.Expenses
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new ExpenseCancelViewModel
            {
                Id = item.Id,
                ExpenseDate = item.ExpenseDate,
                Amount = item.Amount,
                CategoryName = item.ExpenseCategory != null ? item.ExpenseCategory.Name : "—",
                IsCancelled = item.IsCancelled,
                IsAssociationElectricityExpense = item.AssociationElectricityReadingId != null,
                CancellationReason = item.CancellationReason
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public sealed class ExpenseCancelViewModel
    {
        public long Id { get; init; }

        public DateOnly ExpenseDate { get; init; }

        public decimal Amount { get; init; }

        public string CategoryName { get; init; } = string.Empty;

        public bool IsCancelled { get; init; }

        public bool IsAssociationElectricityExpense { get; init; }

        public string? CancellationReason { get; init; }
    }

    public sealed class InputModel
    {
        [StringLength(500, ErrorMessage = "Причина отмены не должна превышать 500 символов.")]
        [Display(Name = "Причина отмены")]
        public string? CancellationReason { get; set; }
    }
}
