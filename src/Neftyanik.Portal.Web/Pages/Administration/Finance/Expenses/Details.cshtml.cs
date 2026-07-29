using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public DetailsModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public ExpenseDetailsViewModel Expense { get; private set; } = new();

    public IReadOnlyList<ExpenseHistoryItemViewModel> History { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var expense = await _dbContext.Expenses
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new ExpenseDetailsViewModel
            {
                Id = item.Id,
                ExpenseDate = item.ExpenseDate,
                Amount = item.Amount,
                CategoryName = item.ExpenseCategory != null ? item.ExpenseCategory.Name : "—",
                Description = item.Description,
                Payee = item.Payee,
                DocumentNumber = item.DocumentNumber,
                IsCancelled = item.IsCancelled,
                CancellationReason = item.CancellationReason,
                CancelledAt = item.CancelledAt,
                UpdatedAt = item.UpdatedAt,
                CreatedAt = item.CreatedAt,
                CreatedByName = item.CreatedByUser != null
                    ? item.CreatedByUser.DisplayName ?? item.CreatedByUser.Email ?? item.CreatedByUser.UserName ?? item.CreatedByUser.Id
                    : item.CreatedByUserId,
                AssociationElectricityReadingId = item.AssociationElectricityReadingId,
                DayConsumption = item.AssociationElectricityReading != null ? item.AssociationElectricityReading.DayConsumption : null,
                NightConsumption = item.AssociationElectricityReading != null ? item.AssociationElectricityReading.NightConsumption : null,
                TotalConsumption = item.AssociationElectricityReading != null ? item.AssociationElectricityReading.TotalConsumption : null,
                AppliedSupplierDayRate = item.AssociationElectricityReading != null ? item.AssociationElectricityReading.AppliedSupplierDayRate : null,
                AppliedSupplierNightRate = item.AssociationElectricityReading != null ? item.AssociationElectricityReading.AppliedSupplierNightRate : null,
                CurrentDayReading = item.AssociationElectricityReading != null ? item.AssociationElectricityReading.CurrentDayReading : null,
                CurrentNightReading = item.AssociationElectricityReading != null ? item.AssociationElectricityReading.CurrentNightReading : null,
                PreviousDayReading = item.AssociationElectricityReading != null ? item.AssociationElectricityReading.PreviousDayReading : null,
                PreviousNightReading = item.AssociationElectricityReading != null ? item.AssociationElectricityReading.PreviousNightReading : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (expense is null)
        {
            return NotFound();
        }

        Expense = expense;
        History = await _dbContext.AuditLogs
            .AsNoTracking()
            .Where(item => item.EntityType == nameof(Expense) && item.EntityId == id.ToString())
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new ExpenseHistoryItemViewModel
            {
                Action = item.Action,
                CreatedAt = item.CreatedAt,
                UserName = item.User != null
                    ? item.User.DisplayName ?? item.User.Email ?? item.User.UserName ?? item.User.Id
                    : item.UserId ?? "—",
                NewValues = item.NewValues
            })
            .ToListAsync(cancellationToken);
        return Page();
    }

    public sealed class ExpenseDetailsViewModel
    {
        public long Id { get; init; }

        public DateOnly ExpenseDate { get; init; }

        public decimal Amount { get; init; }

        public string CategoryName { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string? Payee { get; init; }

        public string? DocumentNumber { get; init; }

        public bool IsCancelled { get; init; }

        public string? CancellationReason { get; init; }

        public DateTimeOffset? CancelledAt { get; init; }

        public DateTimeOffset? UpdatedAt { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public string CreatedByName { get; init; } = string.Empty;

        public long? AssociationElectricityReadingId { get; init; }

        public decimal? PreviousDayReading { get; init; }

        public decimal? CurrentDayReading { get; init; }

        public decimal? DayConsumption { get; init; }

        public decimal? AppliedSupplierDayRate { get; init; }

        public decimal? PreviousNightReading { get; init; }

        public decimal? CurrentNightReading { get; init; }

        public decimal? NightConsumption { get; init; }

        public decimal? AppliedSupplierNightRate { get; init; }

        public decimal? TotalConsumption { get; init; }

        public bool IsAssociationElectricityExpense => AssociationElectricityReadingId.HasValue;

        public bool CanEdit => !IsCancelled && !IsAssociationElectricityExpense;
    }

    public sealed class ExpenseHistoryItemViewModel
    {
        public string Action { get; init; } = string.Empty;

        public DateTimeOffset CreatedAt { get; init; }

        public string UserName { get; init; } = string.Empty;

        public string? NewValues { get; init; }
    }
}
