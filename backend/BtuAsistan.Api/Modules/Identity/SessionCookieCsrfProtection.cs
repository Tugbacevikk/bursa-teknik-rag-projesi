using Microsoft.Net.Http.Headers;

namespace BtuAsistan.Api.Modules.Identity;

// CSRF protection for requests signed in through the session cookie.
//
// A browser attaches the cookie by itself, so another site could try to make a signed-in
// user's browser send a request to this API. SameSite=Strict already keeps the cookie out
// of such requests; this is the second layer. A request that changes something and relies
// on the cookie must carry the X-Requested-With header: a form on another site cannot add
// a header, and a script that tries is stopped by the CORS preflight.
//
// Requests with an Authorization header are left alone: the browser never adds that header
// by itself, so they cannot be forged this way.
public static class SessionCookieCsrfProtection
{
    public const string HeaderName = "X-Requested-With";

    public const string MissingHeaderTitle = "This request needs the X-Requested-With header.";

    public static IApplicationBuilder UseSessionCookieCsrfProtection(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (ReliesOnTheCookie(context.Request)
                && ChangesSomething(context.Request)
                && string.IsNullOrWhiteSpace(context.Request.Headers[HeaderName]))
            {
                await Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: MissingHeaderTitle)
                    .ExecuteAsync(context);
                return;
            }

            await next(context);
        });

    // The same rule as token validation (IdentityModule): the cookie counts only when
    // there is no Authorization header.
    private static bool ReliesOnTheCookie(HttpRequest request) =>
        !request.Headers.ContainsKey(HeaderNames.Authorization)
        && request.Cookies.ContainsKey(SessionCookie.Name);

    // GET, HEAD and OPTIONS only read; an OPTIONS request is also what the CORS preflight
    // sends, and it never carries custom headers.
    private static bool ChangesSomething(HttpRequest request) =>
        !HttpMethods.IsGet(request.Method)
        && !HttpMethods.IsHead(request.Method)
        && !HttpMethods.IsOptions(request.Method);
}
