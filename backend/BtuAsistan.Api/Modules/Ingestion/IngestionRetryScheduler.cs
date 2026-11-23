namespace BtuAsistan.Api.Modules.Ingestion;

// Puts a document back on the queue after a delay, for when the AI service is down or still
// loading its model. Waiting is in memory: if the app stops first, the document is still
// Pending in the database and startup recovery picks it up.
public sealed class IngestionRetryScheduler(
    IIngestionQueue queue,
    IHostApplicationLifetime lifetime,
    ILogger<IngestionRetryScheduler> logger)
{
    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(1);

    public void Schedule(Guid documentId)
    {
        var stopping = lifetime.ApplicationStopping;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Delay, stopping);
                await queue.EnqueueAsync(documentId, stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                // Shutting down; recovery handles it on the next start.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not re-queue document {DocumentId}.", documentId);
            }
        }, stopping);
    }
}
