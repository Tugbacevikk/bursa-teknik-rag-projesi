using System.Security.Claims;
using BtuAsistan.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Api.Modules.Identity;

/// <summary>The account must exist, be active and currently hold one of the roles.</summary>
public sealed class ActiveStaffRequirement(params UserRole[] roles) : IAuthorizationRequirement
{
    public IReadOnlyList<UserRole> Roles { get; } = roles;
}

// Reads the account on every request instead of trusting the role in the token: a token
// lives for up to an hour, and a deactivated or demoted account must lose access at once.
public sealed class ActiveStaffHandler(AppDbContext db) : AuthorizationHandler<ActiveStaffRequirement>
{
    public const string InactiveAccountMessage = "The account no longer exists or is deactivated.";

    public const string PasswordChangeRequiredMessage = "The temporary password must be changed first.";

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ActiveStaffRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimNames.UserId), out var userId))
        {
            return;
        }

        var account = await db.Users
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new { u.Role, u.MustChangePassword })
            .SingleOrDefaultAsync();

        if (account is null)
        {
            // Recognized by SignOutInactiveAccountsResultHandler, which turns it into a 401.
            context.Fail(new AuthorizationFailureReason(this, InactiveAccountMessage));
            return;
        }

        // Checked here, where the account is read anyway, rather than in every endpoint.
        if (account.MustChangePassword
            && (context.Resource as HttpContext)?.GetEndpoint()?.Metadata.GetMetadata<AllowTemporaryPasswordAttribute>() is null)
        {
            context.Fail(new AuthorizationFailureReason(this, PasswordChangeRequiredMessage));
            return;
        }

        if (requirement.Roles.Contains(account.Role))
        {
            context.Succeed(requirement);
        }
    }
}
