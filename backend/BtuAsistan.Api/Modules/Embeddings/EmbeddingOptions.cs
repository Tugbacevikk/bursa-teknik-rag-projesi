using System.ComponentModel.DataAnnotations;

namespace BtuAsistan.Api.Modules.Embeddings;

public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";

    // The model the AI service must report. Vectors of different models cannot be compared,
    // so an answer from another model is refused rather than stored next to the others.
    [Required]
    public string Model { get; init; } = "BAAI/bge-m3";

    // Texts per request. Keeps a single request short on a CPU-only server.
    [Range(1, 256)]
    public int BatchSize { get; init; } = 16;

    // Per request. A batch of long chunks can take tens of seconds on CPU.
    [Range(5, 600)]
    public int TimeoutSeconds { get; init; } = 120;
}
