namespace BtuAsistan.Api.Modules.Audit;

public static class AuditActions
{
    public const string DocumentUploaded = "document.uploaded";
    public const string DocumentReplaced = "document.replaced";
    public const string DocumentDeleted = "document.deleted";
    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserPasswordReset = "user.password_reset";
    public const string UserPasswordChanged = "user.password_changed";
}

public static class AuditEntityTypes
{
    public const string Document = "document";
    public const string User = "user";
}
