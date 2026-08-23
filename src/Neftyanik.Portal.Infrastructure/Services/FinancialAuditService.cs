using System.Security.Claims;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public class FinancialAuditService : IFinancialAuditService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FinancialAuditService(ApplicationDbContext dbContext, IHttpContextAccessor httpContextAccessor)
    {
        _dbContext = dbContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public void Add(
        string action,
        string entityType,
        string entityId,
        string? description = null,
        object? oldValues = null,
        object? newValues = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        var user = _httpContextAccessor.HttpContext?.User;

        var entry = new FinancialAuditLog
        {
            CreatedAtUtc = DateTime.UtcNow,
            UserId = TrimToLength(user?.FindFirstValue(ClaimTypes.NameIdentifier), FinancialAuditLog.UserIdMaxLength),
            UserName = TrimToLength(user?.Identity?.Name, FinancialAuditLog.UserNameMaxLength),
            Action = TrimRequired(action, FinancialAuditLog.ActionMaxLength),
            EntityType = TrimRequired(entityType, FinancialAuditLog.EntityTypeMaxLength),
            EntityId = TrimRequired(entityId, FinancialAuditLog.EntityIdMaxLength),
            Description = TrimToLength(description, FinancialAuditLog.DescriptionMaxLength),
            OldValuesJson = Serialize(oldValues),
            NewValuesJson = Serialize(newValues)
        };

        _dbContext.FinancialAuditLogs.Add(entry);
    }

    private static string TrimRequired(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength].TrimEnd();
    }

    private static string? TrimToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength].TrimEnd();
    }

    private static string? Serialize(object? value)
    {
        return value is null
            ? null
            : JsonSerializer.Serialize(value);
    }
}
