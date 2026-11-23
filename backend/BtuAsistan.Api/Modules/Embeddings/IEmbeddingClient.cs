using Pgvector;

namespace BtuAsistan.Api.Modules.Embeddings;

// Turns texts into vectors. Implemented over HTTP by AiServiceEmbeddingClient; tests can
// substitute their own.
public interface IEmbeddingClient
{
    /// <summary>
    /// Returns one vector per text, in the same order, and the name of the model that made
    /// them. Throws EmbeddingUnavailableException when no usable answer comes back.
    /// </summary>
    Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}

public sealed record EmbeddingResult(string Model, IReadOnlyList<Vector> Vectors);
