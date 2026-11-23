using BtuAsistan.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BtuAsistan.Api.Modules.Chat;

// Deletes the chats idle for longer than Chat:RetentionDays (PLANNING C9, D7): at startup,
// then every Chat:RetentionIntervalHours. Their messages go with them (ON DELETE CASCADE).
// Deleted in batches, so a first run over a large backlog does not hold one long
// transaction against the chats being written.
public sealed class RetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<ChatOptions> options,
    TimeProvider timeProvider,
    ILogger<RetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(settings.RetentionIntervalHours), timeProvider);

        try
        {
            do
            {
                await DeleteExpiredAsync(settings, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; what is left is deleted on the next run.
        }
    }

    private async Task DeleteExpiredAsync(ChatOptions settings, CancellationToken stoppingToken)
    {
        try
        {
            var cutoff = timeProvider.GetUtcNow().AddDays(-settings.RetentionDays);
            var total = 0;

            while (true)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var batch = db.Conversations
                    .Where(c => c.LastMessageAt < cutoff)
                    .OrderBy(c => c.LastMessageAt)
                    .Select(c => c.Id)
                    .Take(settings.RetentionBatchSize);

                var deleted = await db.Conversations
                    .Where(c => batch.Contains(c.Id))
                    .ExecuteDeleteAsync(stoppingToken);

                total += deleted;
                if (deleted < settings.RetentionBatchSize)
                {
                    break;
                }
            }

            if (total > 0)
            {
                logger.LogInformation(
                    "Deleted {Count} chat(s) idle since before {Cutoff:yyyy-MM-dd} ({Days} days).",
                    total, cutoff, settings.RetentionDays);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            // Do not take the whole API down (e.g. the database is not up yet); the next
            // run tries again.
            logger.LogError(ex, "Deleting expired chats failed; trying again in {Hours} hour(s).", settings.RetentionIntervalHours);
        }
    }
}
