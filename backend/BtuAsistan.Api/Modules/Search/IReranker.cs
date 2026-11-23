namespace BtuAsistan.Api.Modules.Search;

// Reorders candidate chunks by reading each one together with the question (PLANNING S5).
// Registered only when the reranker is enabled; ChunkRetrieval works without it.
public interface IReranker
{
    // One relevance score per text, in the same order (higher is more relevant). Throws
    // RerankerUnavailableException when the service cannot answer; search then goes on
    // with the fused order.
    Task<IReadOnlyList<double>> ScoreAsync(string question, IReadOnlyList<string> texts, CancellationToken cancellationToken);
}

public sealed class RerankerUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
