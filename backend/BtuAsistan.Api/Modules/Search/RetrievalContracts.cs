namespace BtuAsistan.Api.Modules.Search;

public enum RetrievalMethod
{
    // Both searches, fused. What students get.
    Hybrid,

    // One search alone; for the evaluation, which compares the three.
    Keyword,
    Vector,
}

// How to search. The defaults are what the chat uses; the evaluation and the search screen
// override them to compare methods.
public sealed record RetrievalRequest(
    string Question,
    RetrievalMethod Method = RetrievalMethod.Hybrid,
    // Null: as configured (Reranker:Enabled).
    bool? UseReranker = null,
    // Null: Search:ResultCount.
    int? ResultCount = null);

public sealed record RetrievalResult(
    IReadOnlyList<RetrievedChunk> Chunks,
    // The vector search was asked for but the AI service could not answer; the result
    // comes from keyword search alone.
    bool VectorSearchFailed,
    // The reranker ordered these chunks. False when it is off, was not asked for, or failed.
    bool Reranked)
{
    public static readonly RetrievalResult Empty = new([], VectorSearchFailed: false, Reranked: false);
}

// One chunk with what the chat and the search screen need to cite it.
public sealed record RetrievedChunk(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentTitle,
    string Unit,
    string? SourceUrl,
    string? ValidityPeriod,
    string? SectionPath,
    int? PageNumber,
    // With the context header ("Belge: … > …"); what the model reads.
    string Content,
    // The final score: RRF, or the reranker's when it ran. Only for ordering and display.
    double Score,
    // 1-based positions in each search, null when that search did not return the chunk.
    // They show why a chunk was found.
    int? KeywordRank,
    int? VectorRank);
