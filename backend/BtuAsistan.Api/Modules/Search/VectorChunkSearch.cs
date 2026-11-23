using BtuAsistan.Api.Modules.Embeddings;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BtuAsistan.Api.Modules.Search;

// Adapted from DocAssistant (#51).
//
// Exact search: the question is compared with every chunk, without a vector index. At the
// school's size (hundreds of documents, thousands of chunks) that takes milliseconds;
// DocAssistant measured 69 ms for 20,000 chunks. An approximate index (HNSW) would be
// faster on far larger tables but can miss the right chunk.
//
// Only chunks embedded by the configured model take part: vectors of different models are
// not in the same space, and a distance between them means nothing. After a model change
// they come back once re-embedded (PLANNING S11).
public sealed class VectorChunkSearch(
    NpgsqlDataSource dataSource,
    IEmbeddingClient embeddingClient,
    IOptions<EmbeddingOptions> embeddingOptions) : IVectorChunkSearch
{
    // <=> is pgvector's cosine distance; vectors are unit length, so 1 - distance is the
    // cosine similarity. Ties are broken by id so the same question gives the same order.
    private const string Sql = """
        SELECT id, 1 - (embedding <=> @question) AS score
        FROM chunks
        WHERE embedding_model = @model
        ORDER BY embedding <=> @question, id
        LIMIT @limit
        """;

    public async Task<IReadOnlyList<RankedChunk>> SearchAsync(
        string question, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question) || limit < 1)
        {
            return [];
        }

        var embedding = await embeddingClient.EmbedAsync([question.Trim()], cancellationToken);

        await using var command = dataSource.CreateCommand(Sql);
        command.Parameters.AddWithValue("question", embedding.Vectors[0]);
        command.Parameters.AddWithValue("model", embeddingOptions.Value.Model);
        command.Parameters.AddWithValue("limit", limit);

        var hits = new List<RankedChunk>(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            hits.Add(new RankedChunk(reader.GetGuid(0), reader.GetDouble(1)));
        }

        return hits;
    }
}
