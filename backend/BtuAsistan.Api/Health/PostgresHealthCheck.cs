using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace BtuAsistan.Api.Health;

public sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand(
                "SELECT extversion FROM pg_extension WHERE extname = 'vector'");
            var version = await command.ExecuteScalarAsync(cancellationToken);

            return version is string v
                ? HealthCheckResult.Healthy($"pgvector {v}")
                : HealthCheckResult.Unhealthy("Connected, but the vector extension is not installed.");
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("Cannot connect to PostgreSQL.", ex);
        }
    }
}
