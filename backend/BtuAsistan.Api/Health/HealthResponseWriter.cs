using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BtuAsistan.Api.Health;

/// <summary>
/// Writes each check's result as JSON instead of the default plain "Healthy" text,
/// so it is visible which dependency is down.
/// </summary>
public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMs = (int)entry.Value.Duration.TotalMilliseconds,
                }),
        });
}
