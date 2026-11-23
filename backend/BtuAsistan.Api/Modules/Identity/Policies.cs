namespace BtuAsistan.Api.Modules.Identity;

public static class Policies
{
    // Any active staff account. Also the fallback: endpoints that say nothing require it.
    public const string Staff = "Staff";

    // Active Admin accounts only.
    public const string Admin = "Admin";
}
