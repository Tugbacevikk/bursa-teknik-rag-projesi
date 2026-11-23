using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace BtuAsistan.Api.Modules.Identity;

// A valid token for a deactivated or deleted account would normally get 403 ("signed in
// but not allowed"). It is answered with 401 and the session cookie is removed instead, so
// the web app treats it like an expired session and shows the sign-in screen.
//
// An account that must change its temporary password gets 403 with a problem body saying
// so, rather than the bare 403 of a missing role.
public sealed class SignOutInactiveAccountsResultHandler : IAuthorizationMiddlewareResultHandler
{
    public const string PasswordChangeRequiredTitle = "Devam etmeden önce parolanızı değiştirmeniz gerekiyor.";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var reasons = authorizeResult.Forbidden
            ? authorizeResult.AuthorizationFailure?.FailureReasons.Where(r => r.Handler is ActiveStaffHandler).ToList() ?? []
            : [];

        if (reasons.Any(r => r.Message == ActiveStaffHandler.InactiveAccountMessage))
        {
            SessionCookie.Delete(context);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        if (reasons.Any(r => r.Message == ActiveStaffHandler.PasswordChangeRequiredMessage))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: PasswordChangeRequiredTitle)
                .ExecuteAsync(context);
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
