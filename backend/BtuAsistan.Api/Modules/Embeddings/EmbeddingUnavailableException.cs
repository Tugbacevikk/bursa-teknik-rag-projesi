namespace BtuAsistan.Api.Modules.Embeddings;

// The AI service could not be reached, was not ready, or answered with something unusable.
// Not the document's fault: processing is postponed and tried again, never marked Failed.
public sealed class EmbeddingUnavailableException : Exception
{
    public EmbeddingUnavailableException(string message)
        : base(message)
    {
    }

    public EmbeddingUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
