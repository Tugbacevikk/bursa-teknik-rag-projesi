namespace BtuAsistan.Api.Modules.Identity;

public static class PasswordRules
{
    public const int MinLength = 12;

    // Bounds the hashing work a single request can cause.
    public const int MaxLength = 128;

    public static bool IsValid(string password) =>
        password.Length is >= MinLength and <= MaxLength;
}
