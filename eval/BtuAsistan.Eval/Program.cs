using System.Text;
using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Chat;
using BtuAsistan.Api.Modules.Embeddings;
using BtuAsistan.Api.Modules.Ingestion;
using BtuAsistan.Api.Modules.Ingestion.Parsing;
using BtuAsistan.Api.Modules.Ingestion.Parsing.Docx;
using BtuAsistan.Api.Modules.Ingestion.Parsing.Markdown;
using BtuAsistan.Api.Modules.Ingestion.Parsing.Pdf;
using BtuAsistan.Api.Modules.Search;
using BtuAsistan.Eval;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.Npgsql;

// dotnet run --project eval/BtuAsistan.Eval -- [options]   (see eval/README.md)

Console.OutputEncoding = Encoding.UTF8;

var options = EvalArguments.Parse(args);
if (options is null)
{
    Console.Error.WriteLine(EvalArguments.Usage);
    return 1;
}

var evalFolder = EvalArguments.FindEvalFolder();
var cancellationToken = CancellationToken.None;

CorpusManifest manifest;
QuestionSet set;
var questionsPath = options.QuestionsPath ?? Path.Combine(evalFolder, "questions.json");
try
{
    // Both files are checked before the database is touched or any document processed: a
    // mistake in either is reported in seconds, not after minutes of embedding.
    manifest = EvalCorpus.Load(Path.Combine(evalFolder, "documents.json"));
    set = QuestionSets.Load(questionsPath, manifest.Documents.Select(d => d.Id).ToList());
}
catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

// A reranker method needs the AI service started with the reranker; found out now rather
// than after the other methods have run.
if (options.Methods.Any(m => m.UseReranker))
{
    try
    {
        using var probe = new HttpClient { BaseAddress = new Uri(options.AiServiceUrl), Timeout = TimeSpan.FromMinutes(1) };
        await new AiServiceReranker(probe).ScoreAsync("hazır mı?", ["hazır"], cancellationToken);
    }
    catch (RerankerUnavailableException ex)
    {
        Console.Error.WriteLine($"The reranker is not available ({ex.Message}). Start the AI service with RERANKER_ENABLED=1.");
        return 1;
    }
}

// Answers need the chat model; found out now rather than after the documents are processed.
string? chatModel = null;
if (options.Answers)
{
    using var probe = new HttpClient { BaseAddress = new Uri(options.AiServiceUrl), Timeout = TimeSpan.FromSeconds(30) };
    using var health = System.Text.Json.JsonDocument.Parse(await probe.GetStringAsync("/health", cancellationToken));
    if (!health.RootElement.TryGetProperty("generator", out var generator) || generator.GetString() != "ok")
    {
        Console.Error.WriteLine("The AI service has no chat model. Set GEMINI_API_KEY and GEMINI_MODEL in .env.");
        return 1;
    }

    chatModel = health.RootElement.GetProperty("generator_model").GetString();
}

// 1. Database and documents.
await EvalDatabase.PrepareAsync(options.ConnectionString, options.Reset, cancellationToken);
await using var services = BuildServices(options);

Guid uploaderId;
await using (var scope = services.CreateAsyncScope())
{
    uploaderId = await EvalDatabase.MigrateAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), cancellationToken);
}

Console.WriteLine($"Documents ({manifest.Documents.Count}):");
var documents = await EvalCorpus.SyncAsync(manifest, evalFolder, uploaderId, services, cancellationToken);

// 2. Evidence, checked against the processed documents before anything is measured.
int chunkCount;
await using (var scope = services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    chunkCount = await db.Chunks.CountAsync(cancellationToken);

    var missing = await Evaluator.FindMissingEvidenceAsync(set, documents, db, cancellationToken);
    if (missing.Count > 0)
    {
        Console.Error.WriteLine($"Fix these in {Path.GetFileName(questionsPath)} first:");
        missing.ForEach(m => Console.Error.WriteLine($"  {m}"));
        return 1;
    }
}

var answerable = set.Questions.Count(q => q.Sources.Count > 0);
Console.WriteLine($"Questions: {set.Questions.Count} ({answerable} with sources){(set.Draft ? " — DRAFT, not reviewed yet" : "")}");

// 3. Measure.
if (options.Answers)
{
    var answers = await AnswerEvaluator.RunAsync(set, documents, services, options.AnswerDelay, cancellationToken);
    AnswerEvaluator.Print(answers);

    var answersPath = Report.Write(Path.Combine(evalFolder, "results"), options.Label ?? "answers", new
    {
        RunAt = DateTimeOffset.Now,
        options.Label,
        Mode = "answers",
        QuestionSet = new { set.Version, set.Draft, set.Author, Questions = set.Questions.Count },
        Corpus = new { Documents = documents.Count, Chunks = chunkCount, Ids = documents.Keys.Order() },
        Settings = new
        {
            options.Search.CandidateCount,
            options.Search.ResultCount,
            options.Search.RrfK,
            EmbeddingModel = new EmbeddingOptions().Model,
            ChatModel = chatModel,
        },
        Answers = answers,
    });
    Console.WriteLine();
    Console.WriteLine($"Written to {Path.GetRelativePath(Directory.GetCurrentDirectory(), answersPath)}");
    return 0;
}

var methods = options.Methods.ToList();
var (sources, timings) = await Evaluator.RunAsync(
    set, documents, methods, options.Search.ResultCount, services.GetRequiredService<IChunkRetrieval>(), cancellationToken);

Report.Print(sources, methods, timings);

var path = Report.Write(Path.Combine(evalFolder, "results"), options.Label, new
{
    RunAt = DateTimeOffset.Now,
    options.Label,
    QuestionSet = new { set.Version, set.Draft, set.Author, Questions = set.Questions.Count, Sources = sources.Count },
    Corpus = new { Documents = documents.Count, Chunks = chunkCount, Ids = documents.Keys.Order() },
    Settings = new
    {
        options.Search.CandidateCount,
        options.Search.ResultCount,
        options.Search.RrfK,
        options.Search.RerankCandidateCount,
        options.Search.KeywordPrefixLength,
        EmbeddingModel = new EmbeddingOptions().Model,
    },
    Methods = methods.Select(m =>
    {
        var timing = timings.Single(t => t.Method == m.Name);
        return new
        {
            m.Name,
            Overall = Metrics.Of(sources.Select(s => s.Ranks[m.Name])),
            ByType = sources.GroupBy(s => s.Type).ToDictionary(g => g.Key, g => Metrics.Of(g.Select(s => s.Ranks[m.Name]))),
            timing.AverageMs,
            timing.MaxMs,
        };
    }),
    Sources = sources,
});
Console.WriteLine();
Console.WriteLine($"Written to {Path.GetRelativePath(Directory.GetCurrentDirectory(), path)}");
return 0;

// The pieces of the API the evaluation needs, wired by hand: the API's own module
// registrations would also start its background workers and web features.
static ServiceProvider BuildServices(EvalOptions options)
{
    var services = new ServiceCollection();
    services.AddLogging(logging => logging.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning));

    var dataSourceBuilder = new NpgsqlDataSourceBuilder(options.ConnectionString);
    dataSourceBuilder.UseVector();
    var dataSource = dataSourceBuilder.Build();
    services.AddSingleton(dataSource);
    services.AddDbContext<AppDbContext>(db => db
        .UseNpgsql(dataSource, npgsql => npgsql.UseVector())
        .UseSnakeCaseNamingConvention());

    services.AddSingleton(TimeProvider.System);
    services.AddSingleton<IDocumentParser, MarkdownDocumentParser>();
    services.AddSingleton<IDocumentParser, DocxDocumentParser>();
    services.AddSingleton<IDocumentParser, PdfDocumentParser>();
    services.AddSingleton<DocumentParsers>();
    services.AddScoped<IDocumentProcessor, DocumentProcessor>();
    services.AddScoped<ChunkReembedder>();

    var embeddingOptions = Options.Create(new EmbeddingOptions());
    services.AddSingleton(embeddingOptions);
    services.AddSingleton<IEmbeddingClient>(new AiServiceEmbeddingClient(
        new HttpClient { BaseAddress = new Uri(options.AiServiceUrl), Timeout = TimeSpan.FromSeconds(embeddingOptions.Value.TimeoutSeconds) },
        embeddingOptions,
        NullLogger<AiServiceEmbeddingClient>.Instance));

    services.AddSingleton(Options.Create(options.Search));
    services.AddSingleton<IKeywordChunkSearch, KeywordChunkSearch>();
    services.AddTransient<IVectorChunkSearch, VectorChunkSearch>();
    services.AddSingleton<IRankFusion>(new ReciprocalRankFusion(options.Search.RrfK));
    services.AddTransient<IChunkRetrieval, ChunkRetrieval>();

    if (options.Answers)
    {
        services.AddSingleton(Options.Create(new ChatOptions()));
        services.AddSingleton<IAnswerGenerator>(new AiServiceAnswerGenerator(
            new HttpClient { BaseAddress = new Uri(options.AiServiceUrl), Timeout = TimeSpan.FromSeconds(new ChatOptions().GenerationTimeoutSeconds) }));
        services.AddScoped<ChatService>();
    }

    if (options.Methods.Any(m => m.UseReranker))
    {
        services.AddSingleton<IReranker>(new AiServiceReranker(
            new HttpClient { BaseAddress = new Uri(options.AiServiceUrl), Timeout = TimeSpan.FromMinutes(5) }));
    }

    return services.BuildServiceProvider();
}
