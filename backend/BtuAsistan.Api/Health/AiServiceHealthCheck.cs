using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BtuAsistan.Api.Health;

public sealed class AiServiceHealthCheck(HttpClient httpClient) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("/health", cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"AI service returned {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Cannot reach the AI service.", ex);
        }
    }
}
