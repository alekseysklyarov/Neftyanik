using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using System.Text.Json;

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
        var financialHistory = await _dbContext.FinancialAuditLogs
            .AsNoTracking()
            .Where(item => item.EntityType == nameof(Expense) && item.EntityId == id.ToString())
            .Select(item => new ExpenseHistoryItemViewModel
            {
                Action = item.Action,
                CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(item.CreatedAtUtc, DateTimeKind.Utc)),
                UserName = item.UserName ?? item.UserId ?? "—",
                Description = item.Description ?? BuildFallbackDescription(item.Action, id),
                Changes = BuildChanges(item.OldValuesJson, item.NewValuesJson)
            })
            .ToListAsync(cancellationToken);

        var legacyHistory = await _dbContext.AuditLogs
            .AsNoTracking()
            .Where(item => item.EntityType == nameof(Expense) && item.EntityId == id.ToString())
            .Select(item => new ExpenseHistoryItemViewModel
            {
                Action = NormalizeLegacyAction(item.Action),
                CreatedAt = item.CreatedAt,
                UserName = item.User != null
                    ? item.User.DisplayName ?? item.User.Email ?? item.User.UserName ?? item.User.Id
                    : item.UserId ?? "—",
                Description = BuildFallbackDescription(item.Action, id),
                Changes = BuildChanges(null, item.NewValues)
            })
            .ToListAsync(cancellationToken);

        History = financialHistory
            .Concat(legacyHistory)
            .OrderByDescending(item => item.CreatedAt)
            .ToList();

        return Page();
    }

    private static string NormalizeLegacyAction(string action)
    {
        return action switch
        {
            "Create" => FinancialAuditLogActions.Created,
            "Edit" => FinancialAuditLogActions.Updated,
            "Cancel" => FinancialAuditLogActions.Cancelled,
            "Restore" => FinancialAuditLogActions.Restored,
            _ => action
        };
    }

    private static string BuildFallbackDescription(string action, long expenseId)
    {
        return NormalizeLegacyAction(action) switch
        {
            nameof(FinancialAuditLogActions.Created) => $"Создан расход #{expenseId}.",
            nameof(FinancialAuditLogActions.Updated) => $"Обновлен расход #{expenseId}.",
            nameof(FinancialAuditLogActions.Cancelled) => $"Отменен расход #{expenseId}.",
            nameof(FinancialAuditLogActions.Restored) => $"Восстановлен расход #{expenseId}.",
            _ => $"Изменен расход #{expenseId}."
        };
    }

    private static IReadOnlyList<ExpenseHistoryChangeItemViewModel> BuildChanges(string? oldValuesJson, string? newValuesJson)
    {
        var oldValues = ParseJsonValues(oldValuesJson);
        var newValues = ParseJsonValues(newValuesJson);

        var orderedKeys = new[]
        {
            "ExpenseDate",
            "Amount",
            "ExpenseCategoryId",
            "Description",
            "Payee",
            "DocumentNumber",
            "IsCancelled",
            "CancellationReason",
            "CancelledAt",
            "AssociationElectricityReadingId"
        };

        var items = new List<ExpenseHistoryChangeItemViewModel>();
        foreach (var key in orderedKeys)
        {
            oldValues.TryGetValue(key, out var oldValue);
            newValues.TryGetValue(key, out var newValue);

            if (oldValue is null && newValue is null)
            {
                continue;
            }

            if (oldValue == newValue)
            {
                continue;
            }

            items.Add(new ExpenseHistoryChangeItemViewModel
            {
                Label = GetLabel(key),
                OldValue = oldValue,
                NewValue = newValue
            });
        }

        return items;
    }

    private static Dictionary<string, string?> ParseJsonValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            result[property.Name] = FormatJsonValue(property.Value);
        }

        return result;
    }

    private static string? FormatJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => FormatStringValue(value.GetString()),
            JsonValueKind.Number => value.TryGetDecimal(out var decimalValue)
                ? decimalValue.ToString("0.###")
                : value.GetRawText(),
            JsonValueKind.True => "Да",
            JsonValueKind.False => "Нет",
            _ => value.GetRawText()
        };
    }

    private static string? FormatStringValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParse(value, out var dateOnly))
        {
            return dateOnly.ToString("dd.MM.yyyy");
        }

        if (DateTimeOffset.TryParse(value, out var dateTimeOffset))
        {
            return dateTimeOffset.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        }

        return value;
    }

    private static string GetLabel(string key)
    {
        return key switch
        {
            "ExpenseDate" => "Дата",
            "Amount" => "Сумма",
            "ExpenseCategoryId" => "Тип расхода (ID)",
            "Description" => "Описание",
            "Payee" => "Получатель",
            "DocumentNumber" => "Номер документа",
            "IsCancelled" => "Статус отмены",
            "CancellationReason" => "Причина отмены",
            "CancelledAt" => "Отменен",
            "AssociationElectricityReadingId" => "Показание общего счетчика (ID)",
            _ => key
        };
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

        public string Description { get; init; } = string.Empty;

        public IReadOnlyList<ExpenseHistoryChangeItemViewModel> Changes { get; init; } = [];
    }

    public sealed class ExpenseHistoryChangeItemViewModel
    {
        public string Label { get; init; } = string.Empty;

        public string? OldValue { get; init; }

        public string? NewValue { get; init; }
    }
}
