using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Embeddings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BtuAsistan.Api.Modules.Ingestion;

// Brings chunks embedded by an earlier model up to the configured one (PLANNING S11,
// adapted from DocAssistant #54). Vector search only looks at the configured model's
// vectors, so until then such chunks are found by keyword search alone.
//
// Only the vector is replaced: the stored text is embedded again, the file is not read or
// chunked again, and chunk ids, order and text stay as they are.
public sealed class ChunkReembedder(
    AppDbContext db,
    IEmbeddingClient embeddingClient,
    IOptions<EmbeddingOptions> options)
{
    // Documents with at least one chunk from another model, oldest upload first.
    public async Task<List<Guid>> FindStaleDocumentsAsync(CancellationToken cancellationToken)
    {
        var model = options.Value.Model;

        return await db.Documents
            .Where(d => db.Chunks.Any(c => c.DocumentId == d.Id && c.EmbeddingModel != model))
            .OrderBy(d => d.CreatedAt)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
    }

    // Re-embeds one document's stale chunks and returns how many were updated. Throws
    // EmbeddingUnavailableException when the AI service cannot help.
    public async Task<int> ReembedAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var model = options.Value.Model;

        var stale = await db.Chunks
            .Where(c => c.DocumentId == documentId && c.EmbeddingModel != model)
            .OrderBy(c => c.Ordinal)
            .Select(c => new { c.Id, c.Content, c.EmbeddingModel })
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return 0;
        }

        // The same text and the same client as DocumentProcessor, so a re-embedded chunk gets
        // exactly the vector a freshly processed one would. Before the transaction: no
        // connection is held while the model works.
        var embedding = await embeddingClient.EmbedAsync(
            stale.Select(c => c.Content).ToList(), cancellationToken);

        // One document at a time, all or nothing: if the work stops halfway, finished
        // documents are kept and the rest still look stale on the next start.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var updated = 0;
        for (var i = 0; i < stale.Count; i++)
        {
            var chunk = stale[i];
            var vector = embedding.Vectors[i];

            // Only if the chunk is still there with its old vector. If the document was
            // processed again meanwhile, its old chunks are gone (the new ones already use
            // the configured model) and this updates nothing.
            updated += await db.Chunks
                .Where(c => c.Id == chunk.Id && c.EmbeddingModel == chunk.EmbeddingModel)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(c => c.Embedding, vector)
                    .SetProperty(c => c.EmbeddingModel, embedding.Model), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return updated;
    }
}
