using System.ComponentModel.DataAnnotations;

namespace BtuAsistan.Api.Modules.Analytics;

public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    // Days are counted in the school's time zone: a question at 01:00 belongs to that day,
    // not to the day before as it would in UTC. An IANA id, which .NET also knows on Windows.
    [Required]
    public string TimeZone { get; init; } = "Europe/Istanbul";

    // The longest period one request may cover.
    [Range(1, 3660)]
    public int MaxDays { get; init; } = 366;
}
