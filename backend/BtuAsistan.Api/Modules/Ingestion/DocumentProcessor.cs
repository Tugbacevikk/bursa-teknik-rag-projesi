using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Documents;
using BtuAsistan.Api.Modules.Embeddings;
using BtuAsistan.Api.Modules.Ingestion.Chunking;
using BtuAsistan.Api.Modules.Ingestion.Parsing;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Api.Modules.Ingestion;

// The work on one document: read, chunk, embed, store. Status checks, the attempt limit and
// turning failures into a status are DocumentIngestionHandler's job.
public interface IDocumentProcessor
{
    // Called with the document loaded and in status Processing. On success sets it to Done
    // in the same transaction as its chunks. Throws DocumentParseException when the file
    // cannot be used and EmbeddingUnavailableException when the AI service cannot help.
    Task ProcessAsync(Document document, CancellationToken cancellationToken);
}

// Adapted from DocAssistant.
public sealed class DocumentProcessor(
    AppDbContext db,
    DocumentParsers parsers,
    IEmbeddingClient embeddingClient,
    TimeProvider timeProvider,
    ILogger<DocumentProcessor> logger) : IDocumentProcessor
{
    public const string FileMissingMessage = "Belgenin dosyası bulunamadı. Lütfen belgeyi yeniden yükleyin.";

    public const string NoTextMessage = "Belgede okunabilir metin bulunamadı.";

    public async Task ProcessAsync(Document document, CancellationToken cancellationToken)
    {
        var content = await db.DocumentFiles
            .Where(f => f.DocumentId == document.Id)
            .Select(f => f.Content)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new DocumentParseException(FileMissingMessage);

        var parsed = parsers.Parse(document.ContentType, content);
        var drafts = DocumentChunker.Chunk(document.Title, document.ValidityPeriod, parsed);

        // Readers already reject files without text; a document of headings only would
        // still end up here, and "Done with nothing to search" would hide the problem.
        if (drafts.Count == 0)
        {
            throw new DocumentParseException(NoTextMessage);
        }

        // One vector per chunk, in the same order. The embedded text is the chunk's whole
        // content, context header included: "which document, which section, which year" is
        // often part of the question.
        //
        // Before the transaction: the model can be slow on CPU, and no database connection
        // or lock should be held while waiting for it.
        var embedding = await embeddingClient.EmbedAsync(
            drafts.Select(draft => draft.Content).ToList(), cancellationToken);

        await SaveChunksAsync(document, drafts, embedding, cancellationToken);
    }

    // Old chunks out, new chunks in and Done: all or nothing. If anything fails before the
    // commit, the transaction is rolled back when it is disposed.
    private async Task SaveChunksAsync(
        Document document,
        IReadOnlyList<ChunkDraft> drafts,
        EmbeddingResult embedding,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Marks Done only if the document still holds the file this run read. A new version
        // uploaded, or the document deleted, while the model was working means these chunks
        // belong to nothing: they are dropped and the new version is processed on its own.
        // The update also locks the row until commit, so neither can happen in between.
        var stillCurrent = await db.Documents
            .Where(d => d.Id == document.Id
                && d.Version == document.Version
                && d.ContentHash == document.ContentHash)
            .ExecuteUpdateAsync(set => set
                .SetProperty(d => d.Status, DocumentStatus.Done)
                .SetProperty(d => d.FailureReason, (string?)null)
                .SetProperty(d => d.ProcessedAt, timeProvider.GetUtcNow()), cancellationToken);

        if (stillCurrent == 0)
        {
            logger.LogInformation(
                "Document {DocumentId} changed or was deleted while being processed; discarding version {Version}.",
                document.Id, document.Version);
            return;
        }

        // Left behind by an earlier run cut off after its commit: a document always has
        // exactly one set of chunks.
        await db.Chunks
            .Where(c => c.DocumentId == document.Id)
            .ExecuteDeleteAsync(cancellationToken);

        db.Chunks.AddRange(drafts.Select((draft, index) => new Chunk
        {
            Id = Guid.CreateVersion7(),
            DocumentId = document.Id,
            DocumentVersion = document.Version,
            Ordinal = draft.Ordinal,
            Content = draft.Content,
            PageNumber = draft.PageNumber,
            SectionPath = draft.SectionPath,
            Embedding = embedding.Vectors[index],

            // Tells re-indexing which model made the vector.
            EmbeddingModel = embedding.Model,
        }));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
