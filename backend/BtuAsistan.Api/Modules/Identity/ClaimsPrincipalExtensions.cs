using System.Security.Claims;

namespace BtuAsistan.Api.Modules.Identity;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The signed-in staff member's id. Only for endpoints behind a Staff or Admin policy,
    /// which has already checked that the claim holds an existing account.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimNames.UserId)
            ?? throw new InvalidOperationException("The request has no signed-in user."));
}
