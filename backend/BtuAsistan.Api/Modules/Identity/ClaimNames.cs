namespace BtuAsistan.Api.Modules.Identity;

// Short JWT claim names. Inbound claim mapping is turned off, so these names arrive
// unchanged in HttpContext.User.
public static class ClaimNames
{
    public const string UserId = "sub";
    public const string Email = "email";
    public const string Role = "role";
}
