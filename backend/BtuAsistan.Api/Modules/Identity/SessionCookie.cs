namespace BtuAsistan.Api.Modules.Identity;

// The cookie that carries the access token in a browser. The token itself is unchanged;
// the cookie is only another way of sending it.
//
// HttpOnly: page scripts cannot read it, so a script injected into the page (XSS) cannot
// steal the token. SameSite=Strict: the browser does not send it with requests started by
// other sites. Secure: sent over HTTPS only.
public static class SessionCookie
{
    public const string Name = "btu_asistan_session";

    // Expires together with the token: a cookie that outlives it would only be rejected.
    public static void Append(HttpContext http, AccessToken token) =>
        http.Response.Cookies.Append(Name, token.Token, Options(http, token.ExpiresAt));

    // The browser removes a cookie only when told so with the same name and path it was
    // set with; Delete sends it back empty and already expired.
    public static void Delete(HttpContext http) =>
        http.Response.Cookies.Delete(Name, Options(http, expiresAt: null));

    private static CookieOptions Options(HttpContext http, DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiresAt,

        // Decided by the environment, not by the request scheme: behind a reverse proxy the
        // app itself sees plain HTTP. Only local development on http://localhost goes without.
        Secure = !http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
    };
}
