using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class CancelModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public CancelModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
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

        var currentUser = await _userManager.GetUserAsync(User);
        var isCancelling = !expense.IsCancelled;

        expense.IsCancelled = isCancelling;
        expense.CancellationReason = isCancelling ? Input.CancellationReason?.Trim() : null;
        expense.CancelledAt = isCancelling ? DateTimeOffset.UtcNow : null;
        expense.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (currentUser is not null)
        {
            _dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = currentUser.Id,
                Action = isCancelling ? "Cancel" : "Restore",
                EntityType = nameof(Expense),
                EntityId = expense.Id.ToString(),
                NewValues = JsonSerializer.Serialize(new
                {
                    expense.IsCancelled,
                    expense.CancellationReason,
                    expense.CancelledAt
                }),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        TempData["SuccessMessage"] = expense.IsCancelled
            ? "Расход отменён."
            : "Расход восстановлен.";

        return RedirectToPage("/Administration/Finance/Expenses/Details", new { id });
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
