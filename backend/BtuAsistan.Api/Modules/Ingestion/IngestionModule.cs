using BtuAsistan.Api.Modules.Embeddings;
using BtuAsistan.Api.Modules.Ingestion.Parsing;
using BtuAsistan.Api.Modules.Ingestion.Parsing.Docx;
using BtuAsistan.Api.Modules.Ingestion.Parsing.Markdown;
using BtuAsistan.Api.Modules.Ingestion.Parsing.Pdf;
using Microsoft.Extensions.Options;

namespace BtuAsistan.Api.Modules.Ingestion;

public static class IngestionModule
{
    public static IServiceCollection AddIngestionModule(this IServiceCollection services, string aiServiceUrl)
    {
        // Readers hold no state, so one instance of each serves every document.
        services.AddSingleton<IDocumentParser, MarkdownDocumentParser>();
        services.AddSingleton<IDocumentParser, DocxDocumentParser>();
        services.AddSingleton<IDocumentParser, PdfDocumentParser>();
        services.AddSingleton<DocumentParsers>();

        services.AddOptions<EmbeddingOptions>()
            .BindConfiguration(EmbeddingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient<IEmbeddingClient, AiServiceEmbeddingClient>((provider, client) =>
        {
            client.BaseAddress = new Uri(aiServiceUrl);
            client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value.TimeoutSeconds);
        });

        // One queue for the whole app: the endpoints (through IIngestionQueue) and the
        // worker (through ChannelIngestionQueue) must share the same instance.
        services.AddSingleton<ChannelIngestionQueue>();
        services.AddSingleton<IIngestionQueue>(provider => provider.GetRequiredService<ChannelIngestionQueue>());
        services.AddSingleton<IngestionRetryScheduler>();

        services.AddHostedService<IngestionRecoveryService>();
        services.AddHostedService<IngestionWorker>();
        services.AddHostedService<ReembeddingService>();

        // Scoped: the worker resolves these from a new scope per document, together with
        // that document's AppDbContext.
        services.AddScoped<DocumentIngestionHandler>();
        services.AddScoped<IDocumentProcessor, DocumentProcessor>();
        services.AddScoped<ChunkReembedder>();

        return services;
    }
}
