using Microsoft.Extensions.Options;

namespace BtuAsistan.Api.Modules.Search;

public static class SearchModule
{
    public static IServiceCollection AddSearchModule(
        this IServiceCollection services, IConfiguration configuration, string aiServiceUrl)
    {
        services.AddOptions<SearchOptions>()
            .BindConfiguration(SearchOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Holds no state; the data source it uses is a singleton too.
        services.AddSingleton<IKeywordChunkSearch, KeywordChunkSearch>();

        // Transient like the typed HttpClient behind IEmbeddingClient (IngestionModule).
        services.AddTransient<IVectorChunkSearch, VectorChunkSearch>();

        services.AddSingleton<IRankFusion>(provider =>
            new ReciprocalRankFusion(provider.GetRequiredService<IOptions<SearchOptions>>().Value.RrfK));

        services.AddTransient<IChunkRetrieval, ChunkRetrieval>();

        // IReranker exists only when enabled; ChunkRetrieval takes it as optional. Decided
        // here, at startup, so turning it on or off is a setting plus a restart.
        var reranker = configuration.GetSection(RerankerOptions.SectionName).Get<RerankerOptions>() ?? new RerankerOptions();
        services.AddOptions<RerankerOptions>()
            .BindConfiguration(RerankerOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (reranker.Enabled)
        {
            services.AddHttpClient<IReranker, AiServiceReranker>(client =>
            {
                client.BaseAddress = new Uri(aiServiceUrl);
                client.Timeout = TimeSpan.FromSeconds(reranker.TimeoutSeconds);
            });
        }

        return services;
    }
}
