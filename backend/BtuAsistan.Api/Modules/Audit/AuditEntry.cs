namespace BtuAsistan.Api.Modules.Audit;

/// <summary>
/// One staff action, e.g. a document uploaded or deleted. Rows are only ever added: the app
/// account has no UPDATE or DELETE privilege on this table.
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    // Empty for actions run from the command line (e.g. importing seed documents).
    public Guid? UserId { get; set; }

    // Dotted name, e.g. "document.uploaded".
    public required string Action { get; set; }

    public required string EntityType { get; set; }

    public Guid? EntityId { get; set; }

    // JSON with what is worth keeping once the entity itself is gone (title, version...).
    public string? Details { get; set; }
}
