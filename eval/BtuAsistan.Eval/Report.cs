using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BtuAsistan.Eval;

// Counts per source (PLANNING S10): in how many of them the evidence came back at position
// 1, within the first 5, within the first 8, and the mean reciprocal rank (1/position, 0
// when not found). A question with two sources counts twice.
public sealed record Metrics(int Sources, int Top1, int Top5, int Top8, double Mrr)
{
    public static Metrics Of(IEnumerable<int?> ranks)
    {
        var list = ranks.ToList();
        return new Metrics(
            list.Count,
            list.Count(r => r == 1),
            list.Count(r => r <= 5),
            list.Count(r => r <= 8),
            list.Count == 0 ? 0 : list.Average(r => r is { } p ? 1.0 / p : 0));
    }
}

public static class Report
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web)
    {
        WriteIndented = true,
        // Turkish letters stay readable in the file instead of becoming \u escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Print(IReadOnlyList<SourceResult> sources, IReadOnlyList<EvalMethod> methods, IReadOnlyList<MethodTiming> timings)
    {
        var types = sources.Select(s => s.Type).Distinct().OrderBy(t => Array.IndexOf(QuestionSets.Types, t)).ToList();

        Console.WriteLine();
        Console.WriteLine($"{"",-14}{"kaynak",7}{"ilk 1",8}{"ilk 5",8}{"ilk 8",8}{"MRR",8}{"ort. ms",10}");
        foreach (var method in methods)
        {
            var timing = timings.Single(t => t.Method == method.Name);
            Console.WriteLine(Row(method.Name, Metrics.Of(sources.Select(s => s.Ranks[method.Name])), timing.AverageMs));
        }

        foreach (var type in types)
        {
            Console.WriteLine();
            Console.WriteLine($"[{type}]");
            foreach (var method in methods)
            {
                Console.WriteLine(Row(method.Name, Metrics.Of(sources.Where(s => s.Type == type).Select(s => s.Ranks[method.Name])), null));
            }
        }

        var missed = sources.Where(s => methods.All(m => s.Ranks[m.Name] is null)).ToList();
        if (missed.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Hiçbir yöntemin ilk {8} sonucunda bulunamayan kaynaklar ({missed.Count}):");
            foreach (var source in missed)
            {
                Console.WriteLine($"  {source.QuestionId} ({source.Document}): {source.Evidence}");
            }
        }
    }

    public static string Write(string folder, string? label, object run)
    {
        Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
        var name = string.IsNullOrWhiteSpace(label) ? stamp : $"{stamp}-{Slug(label)}";
        var path = Path.Combine(folder, $"{name}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(run, Json));
        return path;
    }

    private static string Row(string name, Metrics m, double? averageMs) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{name,-14}{m.Sources,7}{m.Top1,8}{m.Top5,8}{m.Top8,8}{m.Mrr,8:0.000}{(averageMs is { } ms ? ms.ToString("0", CultureInfo.InvariantCulture) : ""),10}");

    private static string Slug(string label) =>
        new(label.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
}
