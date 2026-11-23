using NpgsqlTypes;
using Pgvector;

namespace BtuAsistan.Api.Modules.Documents;

public class Chunk
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    // The document version the chunk was made from.
    public int DocumentVersion { get; set; }

    // Position within the document, from 0.
    public int Ordinal { get; set; }

    // Starts with the context heading ("Document > Section"), which is embedded too.
    public required string Content { get; set; }

    // Headings above the chunk, e.g. "İMEP Yönergesi > Başvuru Koşulları"; empty when the
    // document has none.
    public string? SectionPath { get; set; }

    // Filled for PDF; DOCX and Markdown have no fixed pages.
    public int? PageNumber { get; set; }

    // Written together with the chunk: a chunk without a vector is never saved.
    public required Vector Embedding { get; set; }

    // Which model made the vector; vectors of different models cannot be compared, so this
    // tells which chunks to re-embed when the model changes.
    public required string EmbeddingModel { get; set; }

    // The content prepared for Turkish keyword search. Computed by PostgreSQL from Content
    // (a generated column), never written by the app; see ChunkConfiguration.
    public NpgsqlTsVector SearchVector { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
