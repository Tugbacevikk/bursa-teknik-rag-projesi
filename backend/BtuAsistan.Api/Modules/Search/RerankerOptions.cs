using System.ComponentModel.DataAnnotations;

namespace BtuAsistan.Api.Modules.Search;

public sealed class RerankerOptions
{
    public const string SectionName = "Reranker";

    // Off by default (PLANNING S5, Faz 3 step 10): it lifts MRR from 0.72 to 0.82 but adds
    // ~7 s per question on a 16-core CPU; to be revisited on the school's server. Needs the AI service started with RERANKER_ENABLED=1;
    // if it is not, every search falls back to the fused order and logs a warning.
    public bool Enabled { get; init; }

    // Reranking 20 chunks took about 9.5 s on a 16-core CPU (2026-11-17). Past this, search
    // gives up on it and keeps the fused order.
    [Range(1, 300)]
    public int TimeoutSeconds { get; init; } = 30;
}
