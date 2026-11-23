using System.Net;
using System.Net.Http.Json;
using BtuAsistan.Api.Modules.Identity;
using BtuAsistan.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Api.Tests.Integration;

// Faz 1: who may do what (decisions 14–16).
public sealed class AuthTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anonymous_request_to_staff_endpoint_gets_401()
    {
        var response = await api.Anonymous().GetAsync("/api/documents", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Editor_gets_403_on_admin_endpoint()
    {
        var editor = await api.SignInAsNewAsync(UserRole.Editor);

        Assert.Equal(HttpStatusCode.Forbidden, (await editor.GetAsync("/api/users", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/api/documents", Cancel)).StatusCode);
    }

    // The role is read from the database on every request, not from the token.
    [Fact]
    public async Task Demotion_takes_effect_at_once()
    {
        var account = await api.CreateStaffAsync(UserRole.Admin);
        var client = await api.SignInAsync(account);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users", Cancel)).StatusCode);

        await api.WithDbAsync(db => db.Users.Where(u => u.Id == account.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.Role, UserRole.Editor), Cancel));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users", Cancel)).StatusCode);
    }

    // A closed account's token still valid for up to an hour must stop working now, and
    // the browser is told to drop the cookie.
    [Fact]
    public async Task Deactivated_account_gets_401_and_the_cookie_is_removed()
    {
        var account = await api.CreateStaffAsync();
        var client = await api.SignInAsync(account);

        await api.WithDbAsync(db => db.Users.Where(u => u.Id == account.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.IsActive, false), Cancel));
        var response = await client.GetAsync("/api/auth/me", Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{SessionCookie.Name}=;", cookie);
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
    }

    // The same 401 whatever went wrong, so nobody learns which emails have accounts.
    [Fact]
    public async Task Failed_sign_ins_look_the_same()
    {
        var active = await api.CreateStaffAsync();
        var closed = await api.CreateStaffAsync(isActive: false);
        var client = api.Anonymous();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { active.Email, Password = "wrong-password-123" }, Cancel);
        var unknownEmail = await client.PostAsJsonAsync("/api/auth/login", new { Email = "nobody@btu.test", Password = "wrong-password-123" }, Cancel);
        var closedAccount = await client.PostAsJsonAsync("/api/auth/login", new { closed.Email, closed.Password }, Cancel);

        foreach (var response in new[] { wrongPassword, unknownEmail, closedAccount })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("", await response.Content.ReadAsStringAsync(Cancel));
        }
    }

    [Fact]
    public async Task Session_cookie_is_httponly_secure_and_strict()
    {
        var account = await api.CreateStaffAsync();

        var response = await api.Anonymous().PostAsJsonAsync("/api/auth/login", new { account.Email, account.Password }, Cancel);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith($"{SessionCookie.Name}=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    // Decision 16: with the cookie, a change needs X-Requested-With (a form on another
    // site cannot add it); a Bearer token is not sent by browsers on their own, so it is
    // exempt.
    [Fact]
    public async Task Cookie_session_needs_the_csrf_header_to_change_anything()
    {
        var account = await api.CreateStaffAsync();
        var browser = api.Anonymous();
        (await browser.PostAsJsonAsync("/api/auth/login", new { account.Email, account.Password }, Cancel)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/users", Cancel)).StatusCode);

        var withoutHeader = await browser.PostAsJsonAsync("/api/users", new { Email = NewEmail(), Role = "Editor" }, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, withoutHeader.StatusCode);
        Assert.Contains(SessionCookieCsrfProtection.MissingHeaderTitle, await withoutHeader.Content.ReadAsStringAsync(Cancel));

        using var withHeader = new HttpRequestMessage(HttpMethod.Post, "/api/users") { Content = JsonContent.Create(new { Email = NewEmail(), Role = "Editor" }) };
        withHeader.Headers.Add(SessionCookieCsrfProtection.HeaderName, "XMLHttpRequest");
        Assert.Equal(HttpStatusCode.Created, (await browser.SendAsync(withHeader, Cancel)).StatusCode);

        var bearer = await api.SignInAsync(account);
        Assert.Equal(HttpStatusCode.Created, (await bearer.PostAsJsonAsync("/api/users", new { Email = NewEmail(), Role = "Editor" }, Cancel)).StatusCode);
    }

    [Fact]
    public async Task Logout_removes_the_cookie()
    {
        var account = await api.CreateStaffAsync();
        var browser = api.Anonymous();
        (await browser.PostAsJsonAsync("/api/auth/login", new { account.Email, account.Password }, Cancel)).EnsureSuccessStatusCode();

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add(SessionCookieCsrfProtection.HeaderName, "XMLHttpRequest");
        Assert.Equal(HttpStatusCode.NoContent, (await browser.SendAsync(logout, Cancel)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/auth/me", Cancel)).StatusCode);
    }

    private static string NewEmail() => $"yeni-{Guid.NewGuid():N}@btu.test";
}

// Decision 17: 10 sign-in attempts per address per window, then 429 with Retry-After.
public sealed class LoginRateLimitTests(LoginRateLimitTests.Fixture api) : IClassFixture<LoginRateLimitTests.Fixture>
{
    [Fact]
    public async Task Too_many_attempts_get_429_with_retry_after()
    {
        var client = api.Anonymous();
        var cancel = TestContext.Current.CancellationToken;

        for (var i = 0; i < 3; i++)
        {
            var allowed = await client.PostAsJsonAsync("/api/auth/login", new { Email = "x@btu.test", Password = "wrong-password-123" }, cancel);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        var refused = await client.PostAsJsonAsync("/api/auth/login", new { Email = "x@btu.test", Password = "wrong-password-123" }, cancel);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        var retryAfter = int.Parse(Assert.Single(refused.Headers.GetValues("Retry-After")));
        Assert.InRange(retryAfter, 1, 60);
    }

    public sealed class Fixture : ApiFixture
    {
        protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
        {
            ["RateLimiting:LoginPermitLimit"] = "3",
            ["RateLimiting:LoginWindowSeconds"] = "60",
        };
    }
}
