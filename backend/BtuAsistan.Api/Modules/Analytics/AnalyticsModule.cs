using Microsoft.Extensions.Options;

namespace BtuAsistan.Api.Modules.Analytics;

public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services)
    {
        services.AddOptions<AnalyticsOptions>()
            .BindConfiguration(AnalyticsOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => TimeZoneInfo.TryFindSystemTimeZoneById(o.TimeZone, out _), "Analytics:TimeZone is not a known time zone.")
            .ValidateOnStart();

        services.AddSingleton(provider =>
        {
            var id = provider.GetRequiredService<IOptions<AnalyticsOptions>>().Value.TimeZone;
            return new SchoolTimeZone(id, TimeZoneInfo.FindSystemTimeZoneById(id));
        });

        return services;
    }
}

// Analytics:TimeZone, looked up once. Id as configured: Postgres needs the IANA name.
public sealed record SchoolTimeZone(string Id, TimeZoneInfo Zone);
