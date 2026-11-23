using System.Security.Claims;
using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Audit;
using BtuAsistan.Api.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Api.Modules.Identity;

public static class AuthEndpoints
{
    // Static fields initialize in declaration order; UnknownUserHash reads this one.
    private static readonly User UnknownUser = new()
    {
        Email = string.Empty,
        NormalizedEmail = string.Empty,
        PasswordHash = string.Empty,
    };

    // Verified against when the email is unknown, so an unknown and a known email take the
    // same time to reject; otherwise response time would reveal which addresses have accounts.
    private static readonly Lazy<string> UnknownUserHash = new(() =>
        new PasswordHasher<User>().HashPassword(UnknownUser, Guid.NewGuid().ToString()));

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(ApiRateLimiting.LoginPolicy);
        group.MapPost("/logout", Logout).AllowAnonymous();
        group.MapGet("/me", MeAsync)
            .RequireAuthorization(Policies.Staff)
            .AllowTemporaryPassword();

        // The login limit: the current password could otherwise be guessed here.
        group.MapPost("/password", ChangePasswordAsync)
            .RequireAuthorization(Policies.Staff)
            .AllowTemporaryPassword()
            .RequireRateLimiting(ApiRateLimiting.LoginPolicy);

        return app;
    }

    // The account's own password; also how a temporary one is replaced (PLANNING D6).
    private static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == principal.GetUserId(), cancellationToken);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(request.CurrentPassword)
            || request.CurrentPassword.Length > PasswordRules.MaxLength
            || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            errors["currentPassword"] = ["Mevcut parola yanlış."];
        }
        else if (request.NewPassword is null || !PasswordRules.IsValid(request.NewPassword))
        {
            errors["newPassword"] = [$"Yeni parola {PasswordRules.MinLength}–{PasswordRules.MaxLength} karakter olmalıdır."];
        }
        else if (request.NewPassword == request.CurrentPassword)
        {
            errors["newPassword"] = ["Yeni parola mevcut paroladan farklı olmalıdır."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword!);
        user.MustChangePassword = false;
        db.AddAuditEntry(user.Id, AuditActions.UserPasswordChanged, AuditEntityTypes.User, user.Id);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    // Every failure is the same 401: the caller learns neither whether the email exists
    // nor whether the account is deactivated.
    private static async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult>> LoginAsync(
        LoginRequest request,
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        JwtTokenIssuer tokenIssuer,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrEmpty(request.Password)
            || request.Password.Length > PasswordRules.MaxLength)
        {
            return TypedResults.Unauthorized();
        }

        var normalizedEmail = User.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(
            u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is null)
        {
            passwordHasher.VerifyHashedPassword(UnknownUser, UnknownUserHash.Value, request.Password);
            return TypedResults.Unauthorized();
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed || !user.IsActive)
        {
            return TypedResults.Unauthorized();
        }

        // The hash was made with older settings; store one with the current settings.
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(cancellationToken);
        }

        var token = tokenIssuer.Issue(user);
        SessionCookie.Append(http, token);

        // The token is also returned for non-browser clients; the web app does not store it.
        return TypedResults.Ok(new LoginResponse(user.Email, user.Role.ToString(), user.MustChangePassword, token.ExpiresAt, token.Token));
    }

    private static NoContent Logout(HttpContext http)
    {
        SessionCookie.Delete(http);
        return TypedResults.NoContent();
    }

    // The Staff policy has already checked that the account exists and is active. The role
    // comes from the database, not the token, so a change shows up at once.
    private static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> MeAsync(
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = principal.GetUserId();
        var user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(new MeResponse(user.Id, user.Email, user.Role.ToString(), user.MustChangePassword));
    }
}

public sealed record LoginRequest(string Email, string Password);

// MustChangePassword: only /me, /logout and /password work until it is changed.
public sealed record LoginResponse(string Email, string Role, bool MustChangePassword, DateTimeOffset ExpiresAt, string Token);

public sealed record MeResponse(Guid Id, string Email, string Role, bool MustChangePassword);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
