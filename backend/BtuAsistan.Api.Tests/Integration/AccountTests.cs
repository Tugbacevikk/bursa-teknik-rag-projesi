using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BtuAsistan.Api.Modules.Audit;
using BtuAsistan.Api.Modules.Identity;
using BtuAsistan.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Api.Tests.Integration;

// D6: Admins open, change and close staff accounts; temporary passwords.
public sealed class AccountTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static string NewEmail() => $"hesap-{Guid.NewGuid():N}@btu.test";

    private async Task<(Guid Id, string Email, string TemporaryPassword)> OpenAsync(HttpClient admin, string role = "Editor")
    {
        var email = NewEmail();
        var response = await admin.PostAsJsonAsync("/api/users", new { email, role }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<UserCreatedResponse>(Cancel);
        return (created!.User.Id, email, created.TemporaryPassword);
    }

    private async Task<HttpClient> SignInAsync(string email, string password)
    {
        var client = api.Anonymous();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, Cancel);
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(Cancel);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    [Fact]
    public async Task Opened_account_must_change_its_temporary_password_first()
    {
        var admin = await api.SignInAsNewAsync();
        var (_, email, temporary) = await OpenAsync(admin);

        var login = await api.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = temporary }, Cancel);
        Assert.True((await login.Content.ReadFromJsonAsync<LoginResponse>(Cancel))!.MustChangePassword);
        var client = await SignInAsync(email, temporary);

        // Only who-am-I and changing the password work until then.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me", Cancel)).StatusCode);
        var refused = await client.GetAsync("/api/documents", Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains(SignOutInactiveAccountsResultHandler.PasswordChangeRequiredTitle, await refused.Content.ReadAsStringAsync(Cancel));

        var changed = await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = temporary, newPassword = "kendi-parolam-123" }, Cancel);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/documents", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await SignInAsync(email, "kendi-parolam-123")).GetAsync("/api/documents", Cancel)).StatusCode);
    }

    [Theory]
    [InlineData("wrong-current-1", "kendi-parolam-123", "currentPassword")]
    [InlineData(null, "kisa", "newPassword")]
    [InlineData(null, null, "newPassword")]
    public async Task Password_change_is_checked(string? current, string? next, string field)
    {
        var account = await api.CreateStaffAsync();
        var client = await api.SignInAsync(account);

        var response = await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = current ?? account.Password, newPassword = next ?? account.Password }, Cancel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"\"{field}\"", await response.Content.ReadAsStringAsync(Cancel));
    }

    [Fact]
    public async Task Email_already_used_is_refused()
    {
        var admin = await api.SignInAsNewAsync();
        var (_, email, _) = await OpenAsync(admin);

        var again = await admin.PostAsJsonAsync("/api/users", new { email = email.ToUpperInvariant(), role = "Admin" }, Cancel);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    // An Admin cannot lock themselves out: their own role, state and reset are refused.
    [Fact]
    public async Task Admin_cannot_change_their_own_account()
    {
        var self = await api.CreateStaffAsync();
        var client = await api.SignInAsync(self);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/users/{self.Id}", new { role = "Editor" }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/users/{self.Id}", new { isActive = false }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/users/{self.Id}/reset-password", null, Cancel)).StatusCode);
    }

    [Fact]
    public async Task Role_and_state_changes_are_audited_with_before_and_after()
    {
        var admin = await api.CreateStaffAsync();
        var client = await api.SignInAsync(admin);
        var (id, _, _) = await OpenAsync(client);

        var promoted = await client.PatchAsJsonAsync($"/api/users/{id}", new { role = "Admin" }, Cancel);
        await client.PatchAsJsonAsync($"/api/users/{id}", new { isActive = false }, Cancel);

        Assert.Equal("Admin", (await promoted.Content.ReadFromJsonAsync<UserResponse>(Cancel))!.Role);
        var audit = await api.WithDbAsync(db => db.AuditLog
            .Where(a => a.EntityId == id)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Action, a.UserId, a.Details })
            .ToListAsync(Cancel));
        Assert.Equal([AuditActions.UserCreated, AuditActions.UserUpdated, AuditActions.UserUpdated], audit.Select(a => a.Action));
        Assert.All(audit, a => Assert.Equal(admin.Id, a.UserId));

        // jsonb keeps its own spacing and key order, so the details are read, not compared.
        var promotion = JsonDocument.Parse(audit[1].Details!).RootElement;
        Assert.Equal("Editor", promotion.GetProperty("before").GetProperty("role").GetString());
        Assert.Equal("Admin", promotion.GetProperty("after").GetProperty("role").GetString());
        var closing = JsonDocument.Parse(audit[2].Details!).RootElement;
        Assert.True(closing.GetProperty("before").GetProperty("isActive").GetBoolean());
        Assert.False(closing.GetProperty("after").GetProperty("isActive").GetBoolean());
    }

    // A reset password stops working at once; a session opened with the old one may then
    // only change the password.
    [Fact]
    public async Task Password_reset_restricts_open_sessions()
    {
        var admin = await api.SignInAsNewAsync();
        var editor = await api.CreateStaffAsync(UserRole.Editor);
        var session = await api.SignInAsync(editor);

        var reset = await admin.PostAsync($"/api/users/{editor.Id}/reset-password", null, Cancel);
        var temporary = (await reset.Content.ReadFromJsonAsync<PasswordResetResponse>(Cancel))!.TemporaryPassword;

        Assert.Equal(HttpStatusCode.Forbidden, (await session.GetAsync("/api/documents", Cancel)).StatusCode);
        var oldPassword = await api.Anonymous().PostAsJsonAsync("/api/auth/login", new { editor.Email, editor.Password }, Cancel);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        await SignInAsync(editor.Email, temporary);
    }

    [Fact]
    public async Task Closed_account_cannot_sign_in_and_its_session_ends()
    {
        var admin = await api.SignInAsNewAsync();
        var editor = await api.CreateStaffAsync(UserRole.Editor);
        var session = await api.SignInAsync(editor);

        await admin.PatchAsJsonAsync($"/api/users/{editor.Id}", new { isActive = false }, Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/me", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Anonymous().PostAsJsonAsync("/api/auth/login", new { editor.Email, editor.Password }, Cancel)).StatusCode);
    }

    [Fact]
    public async Task Unknown_account_is_404()
    {
        var admin = await api.SignInAsNewAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PatchAsJsonAsync($"/api/users/{Guid.NewGuid()}", new { isActive = false }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/users/{Guid.NewGuid()}/reset-password", null, Cancel)).StatusCode);
    }
}

// The last active Admin cannot be demoted. One after another this cannot happen (whoever
// asks is an Admin too), so it is tried the only way it can: two Admins demoting each
// other at the same moment. The rows are locked (FOR UPDATE), so one of them must lose.
public sealed class LastAdminTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Two_admins_demoting_each_other_at_once_leave_one_admin()
    {
        var cancel = TestContext.Current.CancellationToken;

        // Alone in this database: no other Admin to fall back on.
        await api.WithDbAsync(db => db.Users.ExecuteDeleteAsync(cancel));
        var a = await api.CreateStaffAsync();
        var b = await api.CreateStaffAsync();
        var clientA = await api.SignInAsync(a);
        var clientB = await api.SignInAsync(b);

        var results = await Task.WhenAll(
            clientA.PatchAsJsonAsync($"/api/users/{b.Id}", new { role = "Editor" }, cancel),
            clientB.PatchAsJsonAsync($"/api/users/{a.Id}", new { role = "Editor" }, cancel));

        var statuses = results.Select(r => r.StatusCode).ToList();
        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, statuses);
        Assert.Equal(1, await api.WithDbAsync(db => db.Users.CountAsync(u => u.Role == UserRole.Admin && u.IsActive, cancel)));
    }
}
