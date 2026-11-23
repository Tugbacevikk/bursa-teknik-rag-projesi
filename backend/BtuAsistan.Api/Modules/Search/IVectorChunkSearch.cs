namespace BtuAsistan.Api.Modules.Search;

// Meaning-based search: the question's vector against the chunks' vectors (PLANNING S3).
public interface IVectorChunkSearch
{
    // At most `limit` chunks, closest first; the score is cosine similarity (1 = same
    // direction). Empty for a blank question. Throws EmbeddingUnavailableException when the
    // AI service cannot turn the question into a vector.
    Task<IReadOnlyList<RankedChunk>> SearchAsync(string question, int limit, CancellationToken cancellationToken);
}
