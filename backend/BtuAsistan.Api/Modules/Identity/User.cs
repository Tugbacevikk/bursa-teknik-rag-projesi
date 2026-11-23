namespace BtuAsistan.Api.Modules.Identity;

/// <summary>A staff account. Students do not have accounts; they chat anonymously.</summary>
public class User
{
    public Guid Id { get; set; }

    // As typed; shown back in the UI.
    public required string Email { get; set; }

    // Upper-cased Email; unique and used for lookups.
    public required string NormalizedEmail { get; set; }

    public required string PasswordHash { get; set; }

    public UserRole Role { get; set; }

    // Staff who leave are deactivated rather than deleted, so their audit history stays.
    public bool IsActive { get; set; } = true;

    // Set when an Admin opens the account or resets its password: the temporary password
    // was seen by someone else, so the owner must choose a new one before anything else
    // (PLANNING D6).
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
}
