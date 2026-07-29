using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class IndexModel : PageModel
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? ExpenseCategoryId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "active";

    [BindProperty(SupportsGet = true)]
    public string Kind { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<ExpenseListItemViewModel> Expenses { get; private set; } = [];

    public IReadOnlyList<SelectListItem> ExpenseCategoryOptions { get; private set; } = [];

    public ExpenseSummaryViewModel Summary { get; private set; } = new();

    public bool HasAssociationMeterHistory { get; private set; }

    public int TotalPages { get; private set; }

    public string EmptyStateMessage { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
        Status = NormalizeStatus(Status);
        Kind = NormalizeKind(Kind);
        PageNumber = PageNumber < 1 ? 1 : PageNumber;

        ExpenseCategoryOptions = await _dbContext.ExpenseCategories
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .Select(category => new SelectListItem
            {
                Value = category.Id.ToString(),
                Text = category.Name
            })
            .ToListAsync(cancellationToken);

        HasAssociationMeterHistory = await _dbContext.AssociationElectricityReadings
            .AsNoTracking()
            .AnyAsync(cancellationToken);

        var query = _dbContext.Expenses
            .AsNoTracking()
            .Select(expense => new ExpenseQueryItem
            {
                Id = expense.Id,
                ExpenseCategoryId = expense.ExpenseCategoryId,
                ExpenseDate = expense.ExpenseDate,
                Amount = expense.Amount,
                Description = expense.Description,
                Payee = expense.Payee,
                DocumentNumber = expense.DocumentNumber,
                IsCancelled = expense.IsCancelled,
                CategoryName = expense.ExpenseCategory != null ? expense.ExpenseCategory.Name : "—",
                CreatedByName = expense.CreatedByUser != null
                    ? expense.CreatedByUser.DisplayName ?? expense.CreatedByUser.Email ?? expense.CreatedByUser.UserName ?? expense.CreatedByUser.Id
                    : expense.CreatedByUserId,
                AssociationElectricityReadingId = expense.AssociationElectricityReadingId,
                DayConsumption = expense.AssociationElectricityReading != null ? expense.AssociationElectricityReading.DayConsumption : null,
                NightConsumption = expense.AssociationElectricityReading != null ? expense.AssociationElectricityReading.NightConsumption : null,
                TotalConsumption = expense.AssociationElectricityReading != null ? expense.AssociationElectricityReading.TotalConsumption : null,
                AppliedSupplierDayRate = expense.AssociationElectricityReading != null ? expense.AssociationElectricityReading.AppliedSupplierDayRate : null,
                AppliedSupplierNightRate = expense.AssociationElectricityReading != null ? expense.AssociationElectricityReading.AppliedSupplierNightRate : null,
                CurrentDayReading = expense.AssociationElectricityReading != null ? expense.AssociationElectricityReading.CurrentDayReading : null,
                CurrentNightReading = expense.AssociationElectricityReading != null ? expense.AssociationElectricityReading.CurrentNightReading : null
            });

        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(item => item.CategoryName.Contains(Search)
                || (item.Description != null && item.Description.Contains(Search))
                || (item.Payee != null && item.Payee.Contains(Search))
                || (item.DocumentNumber != null && item.DocumentNumber.Contains(Search)));
        }

        if (ExpenseCategoryId.HasValue)
        {
            query = query.Where(item => item.ExpenseCategoryId == ExpenseCategoryId.Value);
        }

        query = Kind switch
        {
            "electricity" => query.Where(item => item.ExpenseCategoryId == Neftyanik.Portal.Domain.Constants.ExpenseCategoryIds.ElectricityPayment),
            "manual" => query.Where(item => item.ExpenseCategoryId != Neftyanik.Portal.Domain.Constants.ExpenseCategoryIds.ElectricityPayment),
            _ => query
        };

        query = Status switch
        {
            "cancelled" => query.Where(item => item.IsCancelled),
            "all" => query,
            _ => query.Where(item => !item.IsCancelled)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        TotalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)PageSize);
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        Expenses = await query
            .OrderByDescending(item => item.ExpenseDate)
            .ThenByDescending(item => item.Id)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(item => new ExpenseListItemViewModel
            {
                Id = item.Id,
                ExpenseDate = item.ExpenseDate,
                Amount = item.Amount,
                Description = item.Description,
                Payee = item.Payee,
                DocumentNumber = item.DocumentNumber,
                IsCancelled = item.IsCancelled,
                CategoryName = item.CategoryName,
                CreatedByName = item.CreatedByName,
                AssociationElectricityReadingId = item.AssociationElectricityReadingId,
                DayConsumption = item.DayConsumption,
                NightConsumption = item.NightConsumption,
                TotalConsumption = item.TotalConsumption,
                AppliedSupplierDayRate = item.AppliedSupplierDayRate,
                AppliedSupplierNightRate = item.AppliedSupplierNightRate,
                CurrentDayReading = item.CurrentDayReading,
                CurrentNightReading = item.CurrentNightReading
            })
            .ToListAsync(cancellationToken);

        Summary = new ExpenseSummaryViewModel
        {
            TotalActiveExpenses = await _dbContext.Expenses.AsNoTracking().Where(item => !item.IsCancelled).SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m,
            ElectricityExpenses = await _dbContext.Expenses.AsNoTracking().Where(item => !item.IsCancelled && item.ExpenseCategoryId == Neftyanik.Portal.Domain.Constants.ExpenseCategoryIds.ElectricityPayment).SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m,
            ManualExpenses = await _dbContext.Expenses.AsNoTracking().Where(item => !item.IsCancelled && item.ExpenseCategoryId != Neftyanik.Portal.Domain.Constants.ExpenseCategoryIds.ElectricityPayment).SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m,
            ActiveExpensesCount = await _dbContext.Expenses.AsNoTracking().CountAsync(item => !item.IsCancelled, cancellationToken)
        };

        EmptyStateMessage = totalCount == 0 && string.IsNullOrWhiteSpace(Search) && !ExpenseCategoryId.HasValue && Status == "all"
            ? "Расходы пока не зарегистрированы."
            : "По выбранным условиям расходы не найдены.";
    }

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    private static string NormalizeStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "all" => "all",
            "cancelled" => "cancelled",
            _ => "active"
        };
    }

    private static string NormalizeKind(string? kind)
    {
        return kind?.ToLowerInvariant() switch
        {
            "electricity" => "electricity",
            "manual" => "manual",
            _ => "all"
        };
    }

    public sealed class ExpenseSummaryViewModel
    {
        public decimal TotalActiveExpenses { get; init; }

        public decimal ElectricityExpenses { get; init; }

        public decimal ManualExpenses { get; init; }

        public int ActiveExpensesCount { get; init; }
    }

    public sealed class ExpenseListItemViewModel
    {
        public long Id { get; init; }

        public DateOnly ExpenseDate { get; init; }

        public decimal Amount { get; init; }

        public string CategoryName { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string? Payee { get; init; }

        public string? DocumentNumber { get; init; }

        public bool IsCancelled { get; init; }

        public string CreatedByName { get; init; } = string.Empty;

        public long? AssociationElectricityReadingId { get; init; }

        public decimal? DayConsumption { get; init; }

        public decimal? NightConsumption { get; init; }

        public decimal? TotalConsumption { get; init; }

        public decimal? AppliedSupplierDayRate { get; init; }

        public decimal? AppliedSupplierNightRate { get; init; }

        public decimal? CurrentDayReading { get; init; }

        public decimal? CurrentNightReading { get; init; }

        public bool IsAssociationElectricityExpense => AssociationElectricityReadingId.HasValue;

        public bool CanEdit => !IsCancelled && !IsAssociationElectricityExpense;
    }

    private sealed class ExpenseQueryItem
    {
        public long Id { get; init; }

        public int ExpenseCategoryId { get; init; }

        public DateOnly ExpenseDate { get; init; }

        public decimal Amount { get; init; }

        public string CategoryName { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string? Payee { get; init; }

        public string? DocumentNumber { get; init; }

        public bool IsCancelled { get; init; }

        public string CreatedByName { get; init; } = string.Empty;

        public long? AssociationElectricityReadingId { get; init; }

        public decimal? DayConsumption { get; init; }

        public decimal? NightConsumption { get; init; }

        public decimal? TotalConsumption { get; init; }

        public decimal? AppliedSupplierDayRate { get; init; }

        public decimal? AppliedSupplierNightRate { get; init; }

        public decimal? CurrentDayReading { get; init; }

        public decimal? CurrentNightReading { get; init; }
    }
}
