using System.Net;
using System.Net.Http.Json;
using BtuAsistan.Api.Tests.Infrastructure;
using Npgsql;

namespace BtuAsistan.Api.Tests;

// The test setup itself: a migrated database, the restricted account, a signed-in client.
public sealed class InfrastructureTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Api_is_up()
    {
        var response = await api.Anonymous().GetAsync("/health/live", Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The AI service is a fake behind its interfaces, but /health asks the real address, so
    // the whole report is Unhealthy (503); the database line is what this checks.
    [Fact]
    public async Task Health_reports_the_database()
    {
        var response = await api.Anonymous().GetAsync("/health", Cancel);
        var health = await response.Content.ReadFromJsonAsync<HealthReport>(Cancel);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Healthy", health!.Checks["postgres"].Status);
        Assert.Equal("Unhealthy", health.Checks["ai-service"].Status);
    }

    // Decision 12: the API's account reads and writes rows but cannot change the schema.
    [Fact]
    public async Task Restricted_account_cannot_create_tables()
    {
        await using var connection = new NpgsqlConnection(api.Database.AppConnectionString);
        await connection.OpenAsync(Cancel);
        await using var command = new NpgsqlCommand("CREATE TABLE not_allowed (id int)", connection);

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Cancel));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task Staff_member_signs_in_and_is_recognized()
    {
        var account = await api.CreateStaffAsync();
        var client = await api.SignInAsync(account);

        var me = await client.GetFromJsonAsync<Me>("/api/auth/me", Cancel);

        Assert.Equal(account.Email, me!.Email);
        Assert.Equal("Admin", me.Role);
    }

    private sealed record HealthReport(string Status, Dictionary<string, HealthCheck> Checks);

    private sealed record HealthCheck(string Status);

    private sealed record Me(string Email, string Role);
}
