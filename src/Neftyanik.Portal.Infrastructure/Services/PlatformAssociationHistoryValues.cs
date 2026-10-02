using System.Text.Json;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;

namespace Neftyanik.Portal.Infrastructure.Services;

internal static class PlatformAssociationHistoryValues
{
    // Never return raw JSON or unknown fields, including nested objects, to the UI.
    public static IReadOnlyList<PlatformAssociationHistoryChange> Read(string action, string oldJson, string newJson)
    {
        string[] fields = action switch
        {
            PlatformAuditActions.AssociationCreated =>
                ["Name", "Slug", "ContactEmail", "ContactPhone", "PostalAddress", "AdministratorUserName", "AdministratorUserId", "Role"],
            PlatformAuditActions.AssociationEdited => ["Name", "ContactEmail", "ContactPhone", "PostalAddress"],
            PlatformAuditActions.AssociationActivated or PlatformAuditActions.AssociationDeactivated => ["IsActive"],
            PlatformAuditActions.AdministratorAssigned => ["AdministratorUserName", "AdministratorUserId", "Role"],
            _ => []
        };
        try
        {
            using var oldValues = JsonDocument.Parse(oldJson);
            using var newValues = JsonDocument.Parse(newJson);
            if (oldValues.RootElement.ValueKind != JsonValueKind.Object || newValues.RootElement.ValueKind != JsonValueKind.Object)
                return [];

            var changes = new List<PlatformAssociationHistoryChange>();
            foreach (var field in fields)
            {
                var oldValue = ReadValue(oldValues.RootElement, field);
                var newValue = ReadValue(newValues.RootElement, field);
                if (oldValue != newValue)
                    changes.Add(new(field, oldValue, newValue));
            }
            return changes;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? ReadValue(JsonElement values, string field)
    {
        if (!values.TryGetProperty(field, out var value)) return null;
        if (field == "IsActive")
            return value.ValueKind switch { JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null };
        if (field == "Role")
            return value.ValueKind == JsonValueKind.String && value.GetString() == RoleNames.Administrator
                ? RoleNames.Administrator : null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
