using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Documents;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Api.Modules.Ingestion;

// Adapted from DocAssistant, which ran only at startup. Puts unfinished documents back on
// the in-memory queue (PLANNING P10):
// - At startup, Pending and Processing ones: the queue is empty after a restart, and a
//   Processing document was cut off by the stop.
// - Then every few minutes, Pending ones: a message can still be lost while the app runs
//   (the upload request was cancelled while the queue was full). A document that is
//   already queued is harmless to queue again: the handler skips it once it is Done.
//
// A BackgroundService does not hold up startup. That matters: with more unfinished
// documents than the queue holds, enqueueing waits for the worker, which can only run once
// startup has finished.
public sealed class IngestionRecoveryService(
    IServiceScopeFactory scopeFactory,
    IIngestionQueue queue,
    ILogger<IngestionRecoveryService> logger) : BackgroundService
{
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueAsync(includeProcessing: true, stoppingToken);

        using var timer = new PeriodicTimer(SweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RequeueAsync(includeProcessing: false, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task RequeueAsync(bool includeProcessing, CancellationToken stoppingToken)
    {
        try
        {
            var unfinished = await FindUnfinishedDocumentsAsync(includeProcessing, stoppingToken);

            foreach (var documentId in unfinished)
            {
                await queue.EnqueueAsync(documentId, stoppingToken);
            }

            if (unfinished.Count > 0 || includeProcessing)
            {
                logger.LogInformation("Re-queued {Count} unfinished document(s) for ingestion.", unfinished.Count);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; whatever was not re-queued is still in the database.
        }
        catch (Exception ex)
        {
            // Do not take the whole API down (e.g. the database is not up yet): the next
            // sweep tries again.
            logger.LogError(ex, "Could not re-queue unfinished documents.");
        }
    }

    private async Task<List<Guid>> FindUnfinishedDocumentsAsync(bool includeProcessing, CancellationToken cancellationToken)
    {
        // AppDbContext is scoped (one per request); outside a request we make our own scope.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Oldest first, so documents are processed in the order they were uploaded.
        return await db.Documents
            .Where(d => d.Status == DocumentStatus.Pending
                || (includeProcessing && d.Status == DocumentStatus.Processing))
            .OrderBy(d => d.UpdatedAt)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
    }
}
