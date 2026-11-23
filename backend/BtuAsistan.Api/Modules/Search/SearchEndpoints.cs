using System.Diagnostics;
using BtuAsistan.Api.Modules.Identity;
using BtuAsistan.Api.Modules.Ingestion.Chunking;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BtuAsistan.Api.Modules.Search;

// Staff only (PLANNING S6): lets the people who upload documents see what a question finds,
// before students ask it. Students reach search through the chat (Faz 4), never directly.
public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/search", SearchAsync).RequireAuthorization(Policies.Staff);
        return app;
    }

    private static async Task<Results<Ok<SearchResponse>, ValidationProblem>> SearchAsync(
        SearchRequest request, IChunkRetrieval retrieval, CancellationToken cancellationToken)
    {
        var errors = request.Validate(out var method);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await retrieval.RetrieveAsync(
            new RetrievalRequest(request.Question!.Trim(), method, request.UseReranker, request.ResultCount),
            cancellationToken);

        return TypedResults.Ok(new SearchResponse(
            (int)stopwatch.ElapsedMilliseconds,
            result.VectorSearchFailed,
            result.Reranked,
            result.Chunks.Select(chunk => new SearchHit(
                chunk.ChunkId,
                chunk.DocumentId,
                chunk.DocumentTitle,
                chunk.Unit,
                chunk.SourceUrl,
                chunk.ValidityPeriod,
                chunk.SectionPath,
                chunk.PageNumber,
                // What the document says; the context header is for search and the model.
                DocumentChunker.WithoutContextHeader(chunk.Content),
                chunk.Score,
                chunk.KeywordRank,
                chunk.VectorRank)).ToList()));
    }
}

public sealed record SearchRequest(string? Question, string? Method, bool? UseReranker, int? ResultCount)
{
    public Dictionary<string, string[]> Validate(out RetrievalMethod method)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(Question) || Question.Trim().Length > SearchOptions.MaxQuestionLength)
        {
            errors["question"] = [$"Soru boş olamaz ve en fazla {SearchOptions.MaxQuestionLength} karakter olabilir."];
        }

        method = RetrievalMethod.Hybrid;
        if (Method is not null
            && (!Enum.TryParse(Method, ignoreCase: true, out method) || !Enum.IsDefined(method) || int.TryParse(Method, out _)))
        {
            errors["method"] = [$"Yöntem şunlardan biri olmalı: {string.Join(", ", Enum.GetNames<RetrievalMethod>())}."];
        }

        if (ResultCount is < 1 or > 50)
        {
            errors["resultCount"] = ["Sonuç sayısı 1 ile 50 arasında olmalı."];
        }

        return errors;
    }
}

public sealed record SearchResponse(int DurationMs, bool VectorSearchFailed, bool Reranked, IReadOnlyList<SearchHit> Hits);

public sealed record SearchHit(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentTitle,
    string Unit,
    string? SourceUrl,
    string? ValidityPeriod,
    string? SectionPath,
    int? PageNumber,
    string Text,
    double Score,
    int? KeywordRank,
    int? VectorRank);
