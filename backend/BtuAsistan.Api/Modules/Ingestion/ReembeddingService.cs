using BtuAsistan.Api.Modules.Embeddings;

namespace BtuAsistan.Api.Modules.Ingestion;

// Runs once at startup: re-embeds the chunks left from an earlier Embedding:Model, document
// by document (see ChunkReembedder). Changing the model is then a setting and a restart.
// Nothing to do, and it ends at once.
//
// Unlike DocAssistant, which left the work to the next start when the AI service could not
// help, this waits and tries again: the AI service takes minutes to load its model and the
// API is usually up long before it, so the first attempt often fails.
public sealed class ReembeddingService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReembeddingService> logger) : BackgroundService
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!await TryReembedAllAsync(stoppingToken))
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; what is left still looks stale on the next start.
        }
    }

    // True when nothing is left to do; false when it should be tried again later.
    private async Task<bool> TryReembedAllAsync(CancellationToken stoppingToken)
    {
        try
        {
            List<Guid> documents;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                documents = await scope.ServiceProvider.GetRequiredService<ChunkReembedder>()
                    .FindStaleDocumentsAsync(stoppingToken);
            }

            if (documents.Count == 0)
            {
                return true;
            }

            logger.LogInformation("Re-embedding the chunks of {Count} document(s) made with another model.", documents.Count);

            for (var i = 0; i < documents.Count; i++)
            {
                // A scope per document, like ingestion: a fresh AppDbContext for each.
                await using var scope = scopeFactory.CreateAsyncScope();
                var updated = await scope.ServiceProvider.GetRequiredService<ChunkReembedder>()
                    .ReembedAsync(documents[i], stoppingToken);

                logger.LogInformation(
                    "Re-embedded {Chunks} chunk(s) of document {DocumentId} ({Done}/{Total}).",
                    updated, documents[i], i + 1, documents.Count);
            }

            logger.LogInformation("Re-embedding finished.");
            return true;
        }
        catch (EmbeddingUnavailableException ex)
        {
            // Still loading, down, or answering with another model or vector size; the
            // finished documents are kept.
            logger.LogWarning("Re-embedding paused, trying again in {Delay}: {Reason}", RetryDelay, ex.Message);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            // Do not take the whole API down (e.g. the database is not up yet).
            logger.LogError(ex, "Re-embedding failed, trying again in {Delay}.", RetryDelay);
            return false;
        }
    }
}
