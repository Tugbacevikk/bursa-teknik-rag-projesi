using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Documents;
using BtuAsistan.Api.Modules.Embeddings;
using BtuAsistan.Api.Modules.Ingestion.Parsing;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Api.Modules.Ingestion;

// Adapted from DocAssistant. Handles one queue message; scoped, so each document gets its
// own AppDbContext.
//
// Three ways a run can end besides success:
// - The file is unusable (DocumentParseException): Failed, with the reason for the uploader.
// - The AI service is unavailable: back to Pending and retried later. The attempt is not
//   counted, since the document did nothing wrong.
// - Anything else (a bug, the app crashing): the document stays Processing; recovery
//   re-queues it on the next start and the attempt limit stops a document that keeps
//   crashing the app from being retried for ever.
public sealed class DocumentIngestionHandler(
    AppDbContext db,
    IDocumentProcessor processor,
    IngestionRetryScheduler retryScheduler,
    ILogger<DocumentIngestionHandler> logger)
{
    public const int MaxAttempts = 3;

    public const string TooManyAttemptsMessage =
        "Belge birkaç denemede işlenemedi. Lütfen yeniden yükleyin; sorun sürerse yöneticinize bildirin.";

    public const string AiServiceUnavailableMessage =
        "Yapay zekâ servisine şu an ulaşılamıyor. Belge birkaç dakika içinde kendiliğinden yeniden işlenecek.";

    public async Task HandleAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null)
        {
            // Deleted while waiting in the queue.
            logger.LogInformation("Document {DocumentId} no longer exists; skipping.", documentId);
            return;
        }

        // The same document can arrive twice (upload and recovery); finished ones are skipped.
        if (document.Status is DocumentStatus.Done or DocumentStatus.Failed)
        {
            return;
        }

        if (document.AttemptCount >= MaxAttempts)
        {
            logger.LogWarning(
                "Document {DocumentId} reached {MaxAttempts} attempts; marking it Failed.", document.Id, MaxAttempts);
            await MarkFailedAsync(document.Id, document.Version, TooManyAttemptsMessage, cancellationToken);
            return;
        }

        // Saved before processing starts, so an attempt that crashes the app still counts.
        document.AttemptCount++;
        document.Status = DocumentStatus.Processing;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await processor.ProcessAsync(document, cancellationToken);
        }
        catch (DocumentParseException ex)
        {
            logger.LogInformation("Document {DocumentId} could not be read: {Reason}", document.Id, ex.Message);
            await MarkFailedAsync(document.Id, document.Version, ex.Message, cancellationToken);
        }
        catch (EmbeddingUnavailableException ex)
        {
            logger.LogWarning("Document {DocumentId} postponed: {Reason}", document.Id, ex.Message);
            await PostponeAsync(document.Id, cancellationToken);
            retryScheduler.Schedule(document.Id);
        }
    }

    // version: the one this run read. If a new file was uploaded meanwhile, the verdict is
    // about the old file and must not fail the new one.
    private async Task MarkFailedAsync(Guid documentId, int version, string reason, CancellationToken cancellationToken)
    {
        // The processor may have left unsaved changes behind; forget them so only the
        // status change below is written.
        db.ChangeTracker.Clear();

        await db.Documents
            .Where(d => d.Id == documentId && d.Version == version)
            .ExecuteUpdateAsync(set => set
                .SetProperty(d => d.Status, DocumentStatus.Failed)
                .SetProperty(d => d.FailureReason, reason), cancellationToken);
    }

    private async Task PostponeAsync(Guid documentId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();

        // Only while it is still ours: a new version uploaded meanwhile has already reset
        // the status and attempts.
        await db.Documents
            .Where(d => d.Id == documentId && d.Status == DocumentStatus.Processing)
            .ExecuteUpdateAsync(set => set
                .SetProperty(d => d.Status, DocumentStatus.Pending)
                .SetProperty(d => d.AttemptCount, d => d.AttemptCount - 1)
                .SetProperty(d => d.FailureReason, AiServiceUnavailableMessage), cancellationToken);
    }
}
