using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BtuAsistan.Eval;

// One question through the whole chat (PLANNING C14): search, the chat model, citations.
public sealed record AnswerResult(
    string QuestionId,
    string Type,
    string Question,
    string? ExpectedAnswer,
    // Answered, NotFound or Failed.
    string Outcome,
    string Answer,
    // Valid [n] in the answer, distinct; and [n] pointing past the sources given.
    int Citations,
    int InvalidCitations,
    List<AnswerSourceResult> Sources,
    double DurationMs);

// Retrieved: the evidence was among the chunks the model was given. Cited: it was in a
// chunk the answer cites.
public sealed record AnswerSourceResult(string Document, string Evidence, bool Retrieved, bool Cited);

public static partial class AnswerEvaluator
{
    /// <summary>
    /// Asks every question through ChatService, the class the chat endpoint uses, so what
    /// is measured is what students get, personal data masking included. Each question is
    /// a new conversation; the conversations are deleted afterwards.
    /// </summary>
    public static async Task<List<AnswerResult>> RunAsync(
        QuestionSet set,
        IReadOnlyDictionary<string, Guid> documents,
        IServiceProvider services,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        var results = new List<AnswerResult>();
        Console.Write("  answers: ");

        foreach (var question in set.Questions)
        {
            var result = await AskAsync(question, documents, services, cancellationToken);

            // The free Gemini tier answers "overloaded" now and then; one more try later.
            if (result.Outcome == "Failed")
            {
                Console.Write('r');
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                result = await AskAsync(question, documents, services, cancellationToken);
            }

            results.Add(result);
            Console.Write(result.Outcome switch { "Answered" => '.', "NotFound" => 'n', _ => 'F' });
            await Task.Delay(delay, cancellationToken);
        }

        Console.WriteLine();

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Conversations.ExecuteDeleteAsync(cancellationToken);
        return results;
    }

    private static async Task<AnswerResult> AskAsync(
        EvalQuestion question,
        IReadOnlyDictionary<string, Guid> documents,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var chat = scope.ServiceProvider.GetRequiredService<ChatService>();

        var stopwatch = Stopwatch.StartNew();
        var answer = new StringBuilder();
        IReadOnlyList<MessageSource> given = [];
        var outcome = "Failed";

        await foreach (var chatEvent in chat.AskAsync(question.Question, conversationId: null, cancellationToken))
        {
            switch (chatEvent)
            {
                case SourcesFound found:
                    given = found.Sources;
                    break;
                case AnswerTextAdded added:
                    answer.Append(added.Text);
                    break;
                case AnswerCompleted:
                    outcome = "Answered";
                    break;
                case AnswerNotFoundEvent:
                    outcome = "NotFound";
                    break;
            }
        }

        var text = outcome == "Answered" ? answer.ToString() : "";
        var numbers = Citation().Matches(text).Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToList();
        var valid = numbers.Where(n => n >= 1 && n <= given.Count).ToList();

        // The texts of the chunks the model was given, to look for each evidence sentence in.
        var chunkIds = given.Select(s => s.ChunkId).ToList();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var texts = await db.Chunks.AsNoTracking()
            .Where(c => chunkIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => QuestionSets.Normalize(c.Content), cancellationToken);

        bool Contains(MessageSource source, Guid documentId, string evidence) =>
            source.DocumentId == documentId && texts.GetValueOrDefault(source.ChunkId, "").Contains(evidence, StringComparison.Ordinal);

        var sources = question.Sources.Select(s =>
        {
            var documentId = documents[s.Document];
            var evidence = QuestionSets.Normalize(s.Evidence);
            return new AnswerSourceResult(
                s.Document,
                s.Evidence,
                Retrieved: given.Any(g => Contains(g, documentId, evidence)),
                Cited: valid.Any(n => Contains(given[n - 1], documentId, evidence)));
        }).ToList();

        return new AnswerResult(
            question.Id,
            question.Type,
            question.Question,
            question.ExpectedAnswer,
            outcome,
            text,
            valid.Count,
            numbers.Count - valid.Count,
            sources,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    public static void Print(IReadOnlyList<AnswerResult> results)
    {
        var answerable = results.Where(r => r.Type != "unanswerable").ToList();
        var unanswerable = results.Where(r => r.Type == "unanswerable").ToList();

        Console.WriteLine();
        Console.WriteLine($"Kaynaklı sorular: {answerable.Count}");
        Console.WriteLine($"  cevaplandı               {answerable.Count(r => r.Outcome == "Answered"),4}");
        Console.WriteLine($"  en az bir geçerli [n]    {answerable.Count(r => r.Citations > 0),4}");
        Console.WriteLine($"  yanlışlıkla reddedildi   {answerable.Count(r => r.Outcome == "NotFound"),4}");
        Console.WriteLine($"  hata                     {answerable.Count(r => r.Outcome == "Failed"),4}");
        Console.WriteLine($"  geçersiz [n] içeren      {results.Count(r => r.InvalidCitations > 0),4}");

        var sources = answerable.SelectMany(r => r.Sources.Select(s => (r.Type, s))).ToList();
        Console.WriteLine();
        Console.WriteLine($"{"Kaynaklar",-16}{"toplam",8}{"modele verildi",16}{"atıf yapıldı",14}");
        Console.WriteLine(SourceRow("hepsi", sources.Select(x => x.s).ToList()));
        foreach (var group in sources.GroupBy(x => x.Type).OrderBy(g => Array.IndexOf(QuestionSets.Types, g.Key)))
        {
            Console.WriteLine(SourceRow(group.Key, group.Select(x => x.s).ToList()));
        }

        Console.WriteLine();
        Console.WriteLine($"Cevapsız (unanswerable) sorular: {unanswerable.Count}, reddedilen: {unanswerable.Count(r => r.Outcome == "NotFound")}");
        Console.WriteLine($"Soru başına ortalama: {results.Average(r => r.DurationMs) / 1000:0.0} sn, en uzun: {results.Max(r => r.DurationMs) / 1000:0.0} sn");

        var uncited = answerable.Where(r => r.Sources.Any(s => s.Retrieved && !s.Cited)).ToList();
        if (uncited.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Kanıt modele verildiği hâlde atıf yapılmayanlar:");
            foreach (var r in uncited)
            {
                Console.WriteLine($"  {r.QuestionId} ({r.Outcome}): {r.Question}");
            }
        }
    }

    private static string SourceRow(string name, List<AnswerSourceResult> sources) =>
        $"{name,-16}{sources.Count,8}{sources.Count(s => s.Retrieved),16}{sources.Count(s => s.Cited),14}";

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex Citation();
}
