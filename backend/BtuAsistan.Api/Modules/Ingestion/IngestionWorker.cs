namespace BtuAsistan.Api.Modules.Ingestion;

// Adapted from DocAssistant. Reads the ingestion queue for the lifetime of the app and
// processes documents one at a time. The per-document work is in DocumentIngestionHandler.
public sealed class IngestionWorker(
    ChannelIngestionQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var documentId in queue.ReadAllAsync(stoppingToken))
            {
                await HandleAsync(documentId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // App is shutting down. A document cut off mid-processing stays Processing in
            // the database; startup recovery re-queues it on the next start.
        }
    }

    private async Task HandleAsync(Guid documentId, CancellationToken stoppingToken)
    {
        try
        {
            // A new scope per document: its own AppDbContext, so one document's tracked
            // entities never leak into the next.
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<DocumentIngestionHandler>();

            await handler.HandleAsync(documentId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // An unhandled exception in a BackgroundService stops the whole app; one bad
            // document must not do that. The document stays Processing and is retried on
            // the next start, up to the attempt limit.
            logger.LogError(ex, "Ingestion of document {DocumentId} failed unexpectedly.", documentId);
        }
    }
}
