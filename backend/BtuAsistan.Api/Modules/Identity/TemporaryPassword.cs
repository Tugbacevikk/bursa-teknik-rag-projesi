using System.Security.Cryptography;

namespace BtuAsistan.Api.Modules.Identity;

// The password an Admin hands over when opening an account or resetting one (PLANNING D6).
public static class TemporaryPassword
{
    // No 0/O, 1/l/I: it is read off the screen and typed in by someone else.
    private const string Alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    // About 94 bits; above PasswordRules.MinLength.
    public const int Length = 16;

    public static string Create() => RandomNumberGenerator.GetString(Alphabet, Length);
}

// Marks the endpoints an account with MustChangePassword may still use: who am I, sign
// out, change the password. Everything else answers 403 until the password is changed.
public sealed class AllowTemporaryPasswordAttribute : Attribute;

public static class TemporaryPasswordEndpointExtensions
{
    public static TBuilder AllowTemporaryPassword<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AllowTemporaryPasswordAttribute());
}
