using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BtuAsistan.Eval;

// eval/questions.json (format shared with DocAssistant; see eval/README.md).
public sealed record QuestionSet(
    int Version,
    // True until the team has reviewed the questions (PLANNING Faz 3 decisions).
    bool Draft,
    string? Author,
    List<EvalQuestion> Questions);

public sealed record EvalQuestion(
    string Id,
    string Type,
    string Question,
    [property: JsonPropertyName("expected_answer")] string? ExpectedAnswer,
    List<EvalSource> Sources);

public sealed record EvalSource(string Document, string? Section, int? Page, string Evidence);

public static partial class QuestionSets
{
    public static readonly string[] Types =
        ["direct", "paraphrase", "exact_term", "short", "table", "multi_section", "unanswerable"];

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static QuestionSet Load(string path, IReadOnlyCollection<string> documentIds)
    {
        var set = JsonSerializer.Deserialize<QuestionSet>(File.ReadAllText(path), JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"{path} is empty.");

        var problems = new List<string>();
        foreach (var group in set.Questions.GroupBy(q => q.Id).Where(g => g.Count() > 1))
        {
            problems.Add($"duplicate id {group.Key}");
        }

        foreach (var question in set.Questions)
        {
            if (!Types.Contains(question.Type))
            {
                problems.Add($"{question.Id}: unknown type '{question.Type}'");
            }

            if ((question.Type == "unanswerable") != (question.Sources.Count == 0))
            {
                problems.Add($"{question.Id}: unanswerable questions, and only they, have no sources");
            }

            foreach (var source in question.Sources.Where(s => !documentIds.Contains(s.Document)))
            {
                problems.Add($"{question.Id}: unknown document '{source.Document}'");
            }
        }

        return problems.Count == 0
            ? set
            : throw new InvalidOperationException($"{path}:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", problems)}");
    }

    // Evidence is copied from the document by hand; the chunk text has its whitespace and
    // Markdown marks rearranged by the reader. Both are compared without those, and without
    // case (Turkish rules: İ/i, I/ı).
    public static string Normalize(string text)
    {
        var plain = MarkdownMarks().Replace(text, string.Empty);
        return Whitespace().Replace(plain, " ").Trim().ToLower(Turkish);
    }

    [GeneratedRegex(@"[*_`#]")]
    private static partial Regex MarkdownMarks();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
