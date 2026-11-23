using System.Diagnostics;
using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Search;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Eval;

// One way of searching to measure (PLANNING S10).
public sealed record EvalMethod(string Name, RetrievalMethod Method, bool UseReranker);

// Where a source's evidence turned up for one method: the 1-based position of the first
// returned chunk of that document containing it, or null when none did.
public sealed record SourceResult(string QuestionId, string Type, string Document, string Evidence, Dictionary<string, int?> Ranks);

public sealed record MethodTiming(string Method, double AverageMs, double MaxMs);

public static class Evaluator
{
    /// <summary>
    /// Checks first that every evidence sentence occurs in its document at all: a typo in
    /// the evidence would otherwise read as a search failure.
    /// </summary>
    public static async Task<List<string>> FindMissingEvidenceAsync(
        QuestionSet set, IReadOnlyDictionary<string, Guid> documents, AppDbContext db, CancellationToken cancellationToken)
    {
        var chunkTexts = (await db.Chunks.AsNoTracking()
                .Select(c => new { c.DocumentId, c.Content })
                .ToListAsync(cancellationToken))
            .GroupBy(c => c.DocumentId)
            .ToDictionary(g => g.Key, g => g.Select(c => QuestionSets.Normalize(c.Content)).ToList());

        var missing = new List<string>();
        foreach (var question in set.Questions)
        {
            foreach (var source in question.Sources)
            {
                if (!documents.TryGetValue(source.Document, out var documentId))
                {
                    missing.Add($"{question.Id}: document '{source.Document}' was not processed");
                    continue;
                }

                var evidence = QuestionSets.Normalize(source.Evidence);
                if (!chunkTexts.GetValueOrDefault(documentId, []).Any(text => text.Contains(evidence, StringComparison.Ordinal)))
                {
                    missing.Add($"{question.Id}: evidence not found in '{source.Document}': \"{source.Evidence}\"");
                }
            }
        }

        return missing;
    }

    public static async Task<(List<SourceResult> Sources, List<MethodTiming> Timings)> RunAsync(
        QuestionSet set,
        IReadOnlyDictionary<string, Guid> documents,
        IReadOnlyList<EvalMethod> methods,
        int resultCount,
        IChunkRetrieval retrieval,
        CancellationToken cancellationToken)
    {
        var results = set.Questions
            .SelectMany(q => q.Sources.Select(s => new SourceResult(q.Id, q.Type, s.Document, s.Evidence, [])))
            .ToList();
        var timings = new List<MethodTiming>();

        foreach (var method in methods)
        {
            var durations = new List<double>();
            Console.Write($"  {method.Name}: ");

            foreach (var question in set.Questions)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await retrieval.RetrieveAsync(
                    new RetrievalRequest(question.Question, method.Method, method.UseReranker, resultCount),
                    cancellationToken);
                durations.Add(stopwatch.Elapsed.TotalMilliseconds);

                if (method.UseReranker && !result.Reranked && result.Chunks.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"The reranker did not run for {question.Id}; is the AI service started with RERANKER_ENABLED=1?");
                }

                foreach (var source in results.Where(r => r.QuestionId == question.Id))
                {
                    var documentId = documents[source.Document];
                    var evidence = QuestionSets.Normalize(source.Evidence);
                    var position = result.Chunks
                        .Select((chunk, index) => (chunk, index))
                        .FirstOrDefault(c => c.chunk.DocumentId == documentId
                            && QuestionSets.Normalize(c.chunk.Content).Contains(evidence, StringComparison.Ordinal));

                    source.Ranks[method.Name] = position.chunk is null ? null : position.index + 1;
                }

                Console.Write('.');
            }

            Console.WriteLine();
            timings.Add(new MethodTiming(method.Name, durations.Average(), durations.Max()));
        }

        return (results, timings);
    }
}
