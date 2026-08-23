using System.Text.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.FinancialAuditLog;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public DetailsModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public AuditLogDetailsViewModel Entry { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var entry = await _dbContext.FinancialAuditLogs
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new AuditLogDetailsViewModel
            {
                Id = item.Id,
                CreatedAtUtc = item.CreatedAtUtc,
                UserName = item.UserName,
                UserId = item.UserId,
                Action = item.Action,
                EntityType = item.EntityType,
                EntityId = item.EntityId,
                Description = item.Description,
                OldValuesJson = item.OldValuesJson,
                NewValuesJson = item.NewValuesJson
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (entry is null)
        {
            return NotFound();
        }

        entry.OldValues = ParseValueRows(entry.OldValuesJson);
        entry.NewValues = ParseValueRows(entry.NewValuesJson);
        Entry = entry;
        return Page();
    }

    public sealed class AuditLogDetailsViewModel
    {
        public long Id { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        public string? UserName { get; init; }

        public string? UserId { get; init; }

        public string Action { get; init; } = string.Empty;

        public string EntityType { get; init; } = string.Empty;

        public string EntityId { get; init; } = string.Empty;

        public string? Description { get; init; }

        public string? OldValuesJson { get; init; }

        public string? NewValuesJson { get; init; }

        public IReadOnlyList<AuditValueRowViewModel> OldValues { get; set; } = [];

        public IReadOnlyList<AuditValueRowViewModel> NewValues { get; set; } = [];

        public string UserDisplayName => !string.IsNullOrWhiteSpace(UserName)
            ? UserName
            : !string.IsNullOrWhiteSpace(UserId)
                ? UserId
                : "—";
    }

    public sealed class AuditValueRowViewModel
    {
        public string PropertyName { get; init; } = string.Empty;

        public string Value { get; init; } = string.Empty;
    }

    private static IReadOnlyList<AuditValueRowViewModel> ParseValueRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var rows = new List<AuditValueRowViewModel>();
            AppendRows(document.RootElement, null, rows);
            return rows;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void AppendRows(JsonElement element, string? path, ICollection<AuditValueRowViewModel> rows)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var hasProperties = false;
                foreach (var property in element.EnumerateObject())
                {
                    hasProperties = true;
                    AppendRows(property.Value, CombinePath(path, property.Name), rows);
                }

                if (!hasProperties)
                {
                    rows.Add(new AuditValueRowViewModel
                    {
                        PropertyName = path ?? "Value",
                        Value = "{}"
                    });
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    AppendRows(item, $"{path ?? "Value"}[{index}]", rows);
                    index++;
                }

                if (index == 0)
                {
                    rows.Add(new AuditValueRowViewModel
                    {
                        PropertyName = path ?? "Value",
                        Value = "[]"
                    });
                }
                break;
            default:
                rows.Add(new AuditValueRowViewModel
                {
                    PropertyName = path ?? "Value",
                    Value = GetDisplayValue(element)
                });
                break;
        }
    }

    private static string CombinePath(string? currentPath, string nextSegment)
    {
        return string.IsNullOrWhiteSpace(currentPath)
            ? nextSegment
            : $"{currentPath}.{nextSegment}";
    }

    private static string GetDisplayValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => element.ToString()
        };
    }
}
