using System.Text.Json;
using BtuAsistan.Api.Data;

namespace BtuAsistan.Api.Modules.Audit;

public static class AuditLogExtensions
{
    /// <summary>
    /// Adds an entry to the context without saving, so it is written in the same
    /// SaveChanges (one transaction) as the change it describes: either both or neither.
    /// </summary>
    public static void AddAuditEntry(
        this AppDbContext db, Guid? userId, string action, string entityType, Guid? entityId, object? details = null)
    {
        db.AuditLog.Add(new AuditEntry
        {
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details is null ? null : JsonSerializer.Serialize(details, JsonSerializerOptions.Web),
        });
    }
}
