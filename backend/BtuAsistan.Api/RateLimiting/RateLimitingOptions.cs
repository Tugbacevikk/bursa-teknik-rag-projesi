using System.ComponentModel.DataAnnotations;

namespace BtuAsistan.Api.RateLimiting;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    // Sign-in attempts per client IP address within the window.
    [Range(1, 1000)]
    public int LoginPermitLimit { get; init; } = 10;

    [Range(1, 3600)]
    public int LoginWindowSeconds { get; init; } = 60;

    // Anonymous chat questions per client IP address (PLANNING C11): a short limit against
    // scripts, a longer one against steady use of the Gemini quota. Not measured; a whole
    // classroom behind one school address shares them, so watch for 429s in Faz 5.
    [Range(1, 1000)]
    public int ChatPerMinute { get; init; } = 5;

    [Range(1, 10000)]
    public int ChatPerHour { get; init; } = 30;

    // 👍/👎 per client IP address (PLANNING D3). Cheap (no Gemini call), so looser than
    // questions; only keeps a script from filling the table.
    [Range(1, 1000)]
    public int FeedbackPerMinute { get; init; } = 20;
}
