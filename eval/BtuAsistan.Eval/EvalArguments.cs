using System.Globalization;
using BtuAsistan.Api.Modules.Search;

namespace BtuAsistan.Eval;

public sealed record EvalOptions(
    string ConnectionString,
    string AiServiceUrl,
    bool Reset,
    string? QuestionsPath,
    string? Label,
    IReadOnlyList<EvalMethod> Methods,
    SearchOptions Search,
    // --answers: the whole chat instead of search alone (PLANNING C14).
    bool Answers,
    TimeSpan AnswerDelay);

public static class EvalArguments
{
    // The owner account: the tool creates the database and runs migrations. Development
    // defaults (.env.example); never a production server.
    public const string DefaultConnectionString =
        "Host=127.0.0.1;Port=5433;Database=btu_asistan_eval;Username=btu_asistan;Password=change-me";

    public const string DefaultAiServiceUrl = "http://127.0.0.1:8001";

    private static readonly Dictionary<string, EvalMethod> KnownMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["keyword"] = new("keyword", RetrievalMethod.Keyword, UseReranker: false),
        ["vector"] = new("vector", RetrievalMethod.Vector, UseReranker: false),
        ["hybrid"] = new("hybrid", RetrievalMethod.Hybrid, UseReranker: false),
        ["hybrid-rerank"] = new("hybrid-rerank", RetrievalMethod.Hybrid, UseReranker: true),
    };

    public const string Usage = """
        Usage: dotnet run --project eval/BtuAsistan.Eval -- [options]

          --methods keyword,vector,hybrid,hybrid-rerank   what to measure (default: keyword,vector,hybrid)
          --label <text>              name added to the result file
          --questions <path>          question set (default: eval/questions.json)
          --reset                     drop and rebuild the evaluation database
          --candidates <n>            Search:CandidateCount (default 30)
          --rerank-candidates <n>     Search:RerankCandidateCount (default 20)
          --rrf-k <n>                 Search:RrfK (default 5)
          --keyword-prefix <n>        Search:KeywordPrefixLength (default 0: exact stems)
          --db <connection string>    default: btu_asistan_eval on localhost:5433
          --ai <url>                  default: http://127.0.0.1:8001
          --answers                   ask through the chat (search + chat model) and check
                                      citations, instead of measuring search methods
          --answer-delay <seconds>    pause between answers, for the model's rate limit (default 2)

        hybrid-rerank needs the AI service started with RERANKER_ENABLED=1; --answers needs
        GEMINI_API_KEY and GEMINI_MODEL in the AI service.
        """;

    public static EvalOptions? Parse(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("EVAL_DB") ?? DefaultConnectionString;
        var aiServiceUrl = DefaultAiServiceUrl;
        var reset = false;
        var answers = false;
        var answerDelay = 2.0;
        string? questions = null;
        string? label = null;
        var methods = new List<EvalMethod> { KnownMethods["keyword"], KnownMethods["vector"], KnownMethods["hybrid"] };
        var defaults = new SearchOptions();
        int candidates = defaults.CandidateCount, rerankCandidates = defaults.RerankCandidateCount, rrfK = defaults.RrfK;
        var keywordPrefix = defaults.KeywordPrefixLength;

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");

            try
            {
                switch (args[i])
                {
                    case "--reset": reset = true; break;
                    case "--answers": answers = true; break;
                    case "--answer-delay": answerDelay = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--db": connectionString = Next(); break;
                    case "--ai": aiServiceUrl = Next(); break;
                    case "--questions": questions = Path.GetFullPath(Next()); break;
                    case "--label": label = Next(); break;
                    case "--candidates": candidates = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--rerank-candidates": rerankCandidates = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--rrf-k": rrfK = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--keyword-prefix": keywordPrefix = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                    case "--methods":
                        methods = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(name => KnownMethods.TryGetValue(name, out var m) ? m : throw new ArgumentException($"Unknown method '{name}'."))
                            .ToList();
                        break;
                    default:
                        return null;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                Console.Error.WriteLine(ex.Message);
                return null;
            }
        }

        return new EvalOptions(
            connectionString,
            aiServiceUrl,
            reset,
            questions,
            label,
            methods,
            new SearchOptions { CandidateCount = candidates, RerankCandidateCount = rerankCandidates, RrfK = rrfK, KeywordPrefixLength = keywordPrefix },
            answers,
            TimeSpan.FromSeconds(answerDelay));
    }

    // `dotnet run --project` runs from the project folder; the eval folder is found by
    // walking up to the one that holds documents.json.
    public static string FindEvalFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "eval");
            if (File.Exists(Path.Combine(candidate, "documents.json")))
            {
                return candidate;
            }

            if (File.Exists(Path.Combine(folder.FullName, "documents.json")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("Could not find eval/documents.json above the tool's folder.");
    }
}
