using BtuAsistan.Api.Modules.Documents;
using BtuAsistan.Api.Modules.Embeddings;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BtuAsistan.Api.Modules.Search;

// The one entry point to search (PLANNING S1): the chat, the search screen and the
// evaluation all call it, so what is measured is what students get.
public interface IChunkRetrieval
{
    Task<RetrievalResult> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken);
}

// keyword ─┐
//          ├─► RRF ─► (reranker) ─► top N, with document details
// vector  ─┘
public sealed class ChunkRetrieval(
    IKeywordChunkSearch keywordSearch,
    IVectorChunkSearch vectorSearch,
    IRankFusion fusion,
    NpgsqlDataSource dataSource,
    IOptions<SearchOptions> options,
    ILogger<ChunkRetrieval> logger,
    // Registered only when the reranker is enabled (SearchModule).
    IReranker? reranker = null) : IChunkRetrieval
{
    // Only chunks of the document's current version, of documents that are ready. Chunks of
    // an older version are deleted when a new file is uploaded, but a search running at
    // that moment must not return them either.
    private static readonly string DetailsSql = $"""
        SELECT c.id, d.id, d.title, d.unit, d.source_url, d.validity_period,
               c.section_path, c.page_number, c.content
        FROM chunks c
        JOIN documents d ON d.id = c.document_id
        WHERE c.id = ANY(@ids)
          AND d.status = '{nameof(DocumentStatus.Done)}'
          AND c.document_version = d.version
        """;

    public async Task<RetrievalResult> RetrieveAsync(RetrievalRequest request, CancellationToken cancellationToken)
    {
        var question = request.Question?.Trim() ?? string.Empty;
        if (question.Length == 0)
        {
            return RetrievalResult.Empty;
        }

        var settings = options.Value;
        var resultCount = request.ResultCount ?? settings.ResultCount;

        var keywordHits = request.Method is RetrievalMethod.Hybrid or RetrievalMethod.Keyword
            ? await keywordSearch.SearchAsync(question, settings.CandidateCount, cancellationToken)
            : [];

        var (vectorHits, vectorFailed) = request.Method is RetrievalMethod.Hybrid or RetrievalMethod.Vector
            ? await SearchByMeaningAsync(question, settings.CandidateCount, cancellationToken)
            : ([], false);

        var fused = fusion.Fuse([keywordHits, vectorHits]);

        var useReranker = reranker is not null && (request.UseReranker ?? true);
        var candidates = fused.Take(useReranker ? Math.Max(settings.RerankCandidateCount, resultCount) : resultCount).ToList();

        var chunks = await LoadAsync(candidates, Positions(keywordHits), Positions(vectorHits), cancellationToken);

        var reranked = false;
        if (useReranker && chunks.Count > 1)
        {
            (chunks, reranked) = await RerankAsync(question, chunks, cancellationToken);
        }

        return new RetrievalResult(chunks.Take(resultCount).ToList(), vectorFailed, reranked);
    }

    // When the AI service is down, search goes on with keywords alone: an answer from fewer
    // candidates beats no answer.
    private async Task<(IReadOnlyList<RankedChunk> Hits, bool Failed)> SearchByMeaningAsync(
        string question, int limit, CancellationToken cancellationToken)
    {
        try
        {
            return (await vectorSearch.SearchAsync(question, limit, cancellationToken), false);
        }
        catch (EmbeddingUnavailableException ex)
        {
            logger.LogWarning("Vector search skipped, keyword search only: {Reason}", ex.Message);
            return ([], true);
        }
    }

    // A reranker that fails leaves the fused order in place.
    private async Task<(List<RetrievedChunk> Chunks, bool Reranked)> RerankAsync(
        string question, List<RetrievedChunk> chunks, CancellationToken cancellationToken)
    {
        try
        {
            var scores = await reranker!.ScoreAsync(question, chunks.Select(c => c.Content).ToList(), cancellationToken);

            var reordered = chunks
                .Select((chunk, index) => chunk with { Score = scores[index] })
                .OrderByDescending(chunk => chunk.Score)
                .ThenBy(chunk => chunk.ChunkId)
                .ToList();

            return (reordered, true);
        }
        catch (RerankerUnavailableException ex)
        {
            logger.LogWarning("Reranking skipped, fused order kept: {Reason}", ex.Message);
            return (chunks, false);
        }
    }

    // Reads the chunks' text and their documents' details, keeping the fused order. A chunk
    // deleted since the search ran is simply left out.
    private async Task<List<RetrievedChunk>> LoadAsync(
        IReadOnlyList<RankedChunk> ranked,
        IReadOnlyDictionary<Guid, int> keywordPositions,
        IReadOnlyDictionary<Guid, int> vectorPositions,
        CancellationToken cancellationToken)
    {
        if (ranked.Count == 0)
        {
            return [];
        }

        await using var command = dataSource.CreateCommand(DetailsSql);
        command.Parameters.AddWithValue("ids", ranked.Select(r => r.ChunkId).ToArray());

        var rows = new Dictionary<Guid, RetrievedChunk>(ranked.Count);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var chunkId = reader.GetGuid(0);
                rows[chunkId] = new RetrievedChunk(
                    ChunkId: chunkId,
                    DocumentId: reader.GetGuid(1),
                    DocumentTitle: reader.GetString(2),
                    Unit: reader.GetString(3),
                    SourceUrl: reader.IsDBNull(4) ? null : reader.GetString(4),
                    ValidityPeriod: reader.IsDBNull(5) ? null : reader.GetString(5),
                    SectionPath: reader.IsDBNull(6) ? null : reader.GetString(6),
                    PageNumber: reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    Content: reader.GetString(8),
                    Score: 0,
                    KeywordRank: keywordPositions.TryGetValue(chunkId, out var k) ? k : null,
                    VectorRank: vectorPositions.TryGetValue(chunkId, out var v) ? v : null);
            }
        }

        return ranked
            .Where(r => rows.ContainsKey(r.ChunkId))
            .Select(r => rows[r.ChunkId] with { Score = r.Score })
            .ToList();
    }

    private static Dictionary<Guid, int> Positions(IReadOnlyList<RankedChunk> hits) =>
        hits.Select((hit, index) => (hit.ChunkId, Position: index + 1))
            .ToDictionary(pair => pair.ChunkId, pair => pair.Position);
}
