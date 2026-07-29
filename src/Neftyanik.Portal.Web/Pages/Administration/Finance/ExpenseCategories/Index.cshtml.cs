using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ExpenseCategories;

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
    public string Status { get; set; } = "active";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<ExpenseCategoryListItemViewModel> ExpenseCategories { get; private set; } = [];

    public int TotalPages { get; private set; }

    public string EmptyStateMessage { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
        Status = NormalizeStatus(Status);
        PageNumber = PageNumber < 1 ? 1 : PageNumber;

        var query = _dbContext.ExpenseCategories.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            query = query.Where(category => category.Name.Contains(Search)
                || (category.Description != null && category.Description.Contains(Search)));
        }

        query = Status switch
        {
            "archived" => query.Where(category => !category.IsActive),
            "all" => query,
            _ => query.Where(category => category.IsActive)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        TotalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)PageSize);
        if (PageNumber > TotalPages)
        {
            PageNumber = TotalPages;
        }

        ExpenseCategories = await query
            .OrderBy(category => category.Name)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(category => new ExpenseCategoryListItemViewModel
            {
                Id = category.Id,
                Name = category.Name,
                Description = category.Description,
                IsActive = category.IsActive,
                ExpensesCount = category.Expenses.Count()
            })
            .ToListAsync(cancellationToken);

        EmptyStateMessage = totalCount == 0 && string.IsNullOrWhiteSpace(Search) && Status == "all"
            ? AppLocalizer.Get("Типы расходов пока не добавлены.", "Типи витрат ще не додані.", "No expense types have been added yet.")
            : AppLocalizer.Get("По выбранным условиям типы расходов не найдены.", "За вибраними умовами типи витрат не знайдені.", "No expense types were found for the selected criteria.");
    }

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    private static string NormalizeStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "all" => "all",
            "archived" => "archived",
            _ => "active"
        };
    }

    public sealed class ExpenseCategoryListItemViewModel
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string? Description { get; init; }

        public bool IsActive { get; init; }

        public int ExpensesCount { get; init; }

        public bool IsElectricityCategory => Id == ExpenseCategoryIds.ElectricityPayment;
    }
}
