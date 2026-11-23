using System.ComponentModel.DataAnnotations;

namespace BtuAsistan.Api.Modules.Search;

// Values chosen by the evaluation (PLANNING Faz 3, step 10; eval/results).
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    // Hits taken from each search (keyword, vector) before fusion.
    [Range(1, 200)]
    public int CandidateCount { get; init; } = 30;

    // Chunks returned to the caller (the chat, the search screen).
    [Range(1, 50)]
    public int ResultCount { get; init; } = 8;

    // RRF's constant: a hit at position p scores 1 / (k + p). Larger k flattens the
    // difference between the top positions. 5 beat the usual 60 here: with 60, chunks that
    // both searches found only half-well outranked the best hit of a single search.
    [Range(1, 1000)]
    public int RrfK { get; init; } = 5;

    // Fused chunks handed to the reranker, when it runs. Its cost grows with this number;
    // 10 was faster but could not improve the top 8, 20 brought every source into the top 5.
    [Range(1, 200)]
    public int RerankCandidateCount { get; init; } = 20;

    // 0: keyword search matches a question word's stem exactly. N > 0: also any word whose
    // stem starts with the first N letters of it. PostgreSQL's Turkish stemmer does not
    // bring every form of a word to one stem ("başvuru" → "başvur", "başvurusu" →
    // "başvurus"; "kaza" → "kaz", "kazası" → "kazas"), so exact matching misses forms; a
    // prefix finds them but also unrelated words ("kaz" → "kazanmak"). The exact stem is
    // still searched too, so an exact match ranks above a prefix-only one.
    [Range(0, 20)]
    public int KeywordPrefixLength { get; init; }

    // The longest question accepted; longer ones are refused by the endpoints.
    public const int MaxQuestionLength = 2000;
}
