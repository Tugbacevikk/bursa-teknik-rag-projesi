using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BtuAsistan.Api.RateLimiting;

public static class ApiRateLimiting
{
    // Applied to endpoints with RequireRateLimiting(LoginPolicy).
    public const string LoginPolicy = "login";

    // Applied to the anonymous chat endpoint.
    public const string ChatPolicy = "chat";

    // Applied to the anonymous feedback endpoint.
    public const string FeedbackPolicy = "feedback";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            // The default is 503, which would read as "server down" rather than "slow down".
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            // Slows down password guessing. Counted per IP address: the email is in the body,
            // which the limiter cannot read. Behind a reverse proxy every request shares the
            // proxy's address until forwarded headers are set up (Faz 6).
            limiter.AddPolicy(LoginPolicy, httpContext =>
            {
                var options = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // Fixed rather than sliding window: only the fixed window tells how long until
                // the next attempt is allowed, which becomes the Retry-After header.
                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.LoginPermitLimit,
                    Window = TimeSpan.FromSeconds(options.LoginWindowSeconds),
                    QueueLimit = 0,
                });
            });

            // Both windows must allow the question; Retry-After comes from the one that refused.
            limiter.AddPolicy(ChatPolicy, httpContext =>
            {
                var options = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.Get(clientIp, _ => RateLimiter.CreateChained(
                    new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.ChatPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }),
                    new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.ChatPerHour,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                    })));
            });

            limiter.AddPolicy(FeedbackPolicy, httpContext =>
            {
                var options = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.FeedbackPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
        });

        return services;
    }
}
