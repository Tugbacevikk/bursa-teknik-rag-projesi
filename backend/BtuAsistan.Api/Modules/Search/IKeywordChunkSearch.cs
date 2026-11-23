namespace BtuAsistan.Api.Modules.Search;

// PostgreSQL full-text search over chunks (PLANNING S2).
public interface IKeywordChunkSearch
{
    // At most `limit` chunks, best first. Empty when the question has no searchable words
    // (blank, or only stop words such as "ne", "ve").
    Task<IReadOnlyList<RankedChunk>> SearchAsync(string question, int limit, CancellationToken cancellationToken);
}
