using BtuAsistan.Api.Modules.Documents;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BtuAsistan.Api.Modules.Search;

// Adapted from DocAssistant (#49).
//
// The question goes through the same configuration and case folding as the chunks
// (ChunkConfiguration), so "Raporları" in a question finds "rapor" in a chunk. Any one word
// is enough: requiring all of them returned nothing for most questions, because questions
// carry words no document contains ("kaç", "nedir"). Hits are ordered by ts_rank, which
// counts how often the words occur, divided by the chunk's length (normalization 1) so that
// long chunks do not win on size alone.
//
// Plain SQL through the shared data source rather than EF Core: the query is one statement
// that EF cannot express, and the result is not an entity.
public sealed class KeywordChunkSearch(NpgsqlDataSource dataSource, IOptions<SearchOptions> options) : IKeywordChunkSearch
{
    // The question's lexemes are joined with OR and cast to tsquery directly. Passing them
    // through to_tsquery instead would stem the stems a second time ("haftas" → "hafta")
    // and lose matches. quote_literal keeps lexemes with symbols ("imep/b") intact. With a
    // prefix length, each lexeme is "lexeme | first-letters:*" (SearchOptions).
    private static readonly string Sql = $"""
        WITH question AS (
            SELECT string_agg(
                       CASE WHEN @prefix > 0
                            THEN quote_literal(lexeme) || ' | ' || quote_literal(left(lexeme, @prefix)) || ':*'
                            ELSE quote_literal(lexeme)
                       END,
                       ' | ')::tsquery AS query
            FROM unnest(tsvector_to_array(
                to_tsvector('{ChunkConfiguration.SearchConfiguration}'::regconfig,
                            {ChunkConfiguration.TurkishCaseFoldSql("@question")}))) AS lexeme
        )
        SELECT c.id, ts_rank(c.search_vector, question.query, 1) AS score
        FROM chunks c, question
        WHERE question.query IS NOT NULL AND c.search_vector @@ question.query
        ORDER BY score DESC, c.id
        LIMIT @limit
        """;

    public async Task<IReadOnlyList<RankedChunk>> SearchAsync(
        string question, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question) || limit < 1)
        {
            return [];
        }

        await using var command = dataSource.CreateCommand(Sql);
        command.Parameters.AddWithValue("question", question.Trim());
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("prefix", options.Value.KeywordPrefixLength);

        var hits = new List<RankedChunk>(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            hits.Add(new RankedChunk(reader.GetGuid(0), reader.GetFloat(1)));
        }

        return hits;
    }
}
