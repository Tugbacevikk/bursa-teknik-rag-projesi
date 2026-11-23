using System.Net.Mail;
using System.Security.Claims;
using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Audit;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BtuAsistan.Api.Modules.Identity;

// Staff account management; Admin only (PLANNING D6). Accounts are never deleted, only
// deactivated, so their audit history stays.
public static class UserEndpoints
{
    public const string LastAdminMessage = "En az bir aktif Admin hesabı kalmalıdır.";

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization(Policies.Admin);

        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapPatch("/{id:guid}", UpdateAsync);
        group.MapPost("/{id:guid}/reset-password", ResetPasswordAsync);

        return app;
    }

    private static async Task<List<UserResponse>> ListAsync(AppDbContext db, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking()
            .OrderBy(u => u.NormalizedEmail)
            .Select(u => new UserResponse(u.Id, u.Email, u.Role.ToString(), u.IsActive, u.MustChangePassword, u.CreatedAt))
            .ToListAsync(cancellationToken);

    // The temporary password is in this response only; it is not stored or logged anywhere
    // in plain text. The Admin hands it over; the owner replaces it at first sign-in.
    private static async Task<Results<Created<UserCreatedResponse>, ValidationProblem, Conflict<ProblemMessage>>> CreateAsync(
        CreateUserRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request.Validate(out var email, out var role) is { Count: > 0 } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var normalizedEmail = User.NormalizeEmail(email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            return TypedResults.Conflict(new ProblemMessage("Bu e-postayla bir hesap zaten var."));
        }

        var temporaryPassword = TemporaryPassword.Create();
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty,
            Role = role,
            MustChangePassword = true,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        user.PasswordHash = passwordHasher.HashPassword(user, temporaryPassword);

        db.Users.Add(user);
        db.AddAuditEntry(principal.GetUserId(), AuditActions.UserCreated, AuditEntityTypes.User, user.Id,
            new { user.Email, Role = user.Role.ToString() });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Opened by another request in the meantime.
            return TypedResults.Conflict(new ProblemMessage("Bu e-postayla bir hesap zaten var."));
        }

        return TypedResults.Created($"/api/users/{user.Id}", new UserCreatedResponse(ToResponse(user), temporaryPassword));
    }

    // Role and/or active state. An Admin cannot change their own (they could lock
    // themselves out), and the last active Admin cannot be demoted or deactivated.
    private static async Task<Results<Ok<UserResponse>, NotFound, ValidationProblem, Conflict<ProblemMessage>>> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (request.Validate(out var role) is { Count: > 0 } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var adminId = principal.GetUserId();
        if (id == adminId)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [""] = ["Kendi rolünüzü değiştiremez ya da hesabınızı kapatamazsınız; başka bir Admin yapabilir."],
            });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Locks the active Admin rows until the transaction ends: two Admins demoting each
        // other at the same moment would otherwise both see "one more Admin is left".
        await db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM users WHERE role = 'Admin' AND is_active FOR UPDATE", cancellationToken);

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        var before = new { Role = user.Role.ToString(), user.IsActive };
        user.Role = role ?? user.Role;
        user.IsActive = request.IsActive ?? user.IsActive;

        var losesAdmin = before.Role == nameof(UserRole.Admin) && before.IsActive
            && (user.Role != UserRole.Admin || !user.IsActive);
        if (losesAdmin && await db.Users.CountAsync(u => u.Role == UserRole.Admin && u.IsActive && u.Id != id, cancellationToken) == 0)
        {
            return TypedResults.Conflict(new ProblemMessage(LastAdminMessage));
        }

        var after = new { Role = user.Role.ToString(), user.IsActive };
        if (after != before)
        {
            db.AddAuditEntry(adminId, AuditActions.UserUpdated, AuditEntityTypes.User, user.Id,
                new { user.Email, Before = before, After = after });
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(user));
    }

    // A new temporary password, e.g. when someone forgot theirs. Sessions signed in with the
    // old one can only change the password from now on.
    private static async Task<Results<Ok<PasswordResetResponse>, NotFound, ValidationProblem>> ResetPasswordAsync(
        Guid id,
        ClaimsPrincipal principal,
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        CancellationToken cancellationToken)
    {
        var adminId = principal.GetUserId();
        if (id == adminId)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [""] = ["Kendi parolanızı \"Parolayı değiştir\" ile değiştirin."],
            });
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        var temporaryPassword = TemporaryPassword.Create();
        user.PasswordHash = passwordHasher.HashPassword(user, temporaryPassword);
        user.MustChangePassword = true;
        db.AddAuditEntry(adminId, AuditActions.UserPasswordReset, AuditEntityTypes.User, user.Id, new { user.Email });
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new PasswordResetResponse(temporaryPassword));
    }

    private static UserResponse ToResponse(User u) =>
        new(u.Id, u.Email, u.Role.ToString(), u.IsActive, u.MustChangePassword, u.CreatedAt);

    internal static bool TryParseRole(string? value, out UserRole role) =>
        Enum.TryParse(value, ignoreCase: true, out role) && value!.All(char.IsAsciiLetter);
}

public sealed record UserResponse(Guid Id, string Email, string Role, bool IsActive, bool MustChangePassword, DateTimeOffset CreatedAt);

public sealed record UserCreatedResponse(UserResponse User, string TemporaryPassword);

public sealed record PasswordResetResponse(string TemporaryPassword);

public sealed record ProblemMessage(string Title);

public sealed record CreateUserRequest(string? Email, string? Role)
{
    public Dictionary<string, string[]> Validate(out string email, out UserRole role)
    {
        var errors = new Dictionary<string, string[]>();
        email = Email?.Trim() ?? "";

        if (email.Length is 0 or > UserConfiguration.EmailMaxLength
            || !MailAddress.TryCreate(email, out var address)
            || address.Address != email)
        {
            errors["email"] = ["Geçerli bir e-posta adresi girin."];
        }

        if (!UserEndpoints.TryParseRole(Role, out role))
        {
            errors["role"] = ["Rol \"Admin\" ya da \"Editor\" olmalıdır."];
        }

        return errors;
    }
}

// Fields left out (null) stay as they are.
public sealed record UpdateUserRequest(string? Role, bool? IsActive)
{
    public Dictionary<string, string[]> Validate(out UserRole? role)
    {
        var errors = new Dictionary<string, string[]>();
        role = null;

        if (Role is not null)
        {
            if (UserEndpoints.TryParseRole(Role, out var parsed))
            {
                role = parsed;
            }
            else
            {
                errors["role"] = ["Rol \"Admin\" ya da \"Editor\" olmalıdır."];
            }
        }

        if (Role is null && IsActive is null)
        {
            errors[""] = ["Değiştirilecek bir alan (role, isActive) gönderin."];
        }

        return errors;
    }
}
