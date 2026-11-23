using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BtuAsistan.Api.Modules.Documents;

public class ChunkConfiguration : IEntityTypeConfiguration<Chunk>
{
    // bge-m3 produces 1024 numbers per text. A different model needs a migration.
    public const int EmbeddingDimensions = 1024;

    // PostgreSQL's text search configuration for Turkish. Questions must be parsed with the
    // same configuration and the same case folding, or their words will not match.
    public const string SearchConfiguration = "turkish";

    // SQL that maps I → ı and İ → i in the given expression (see SearchVector).
    public static string TurkishCaseFoldSql(string expression) => $"translate({expression}, 'Iİ', 'ıi')";

    public void Configure(EntityTypeBuilder<Chunk> builder)
    {
        builder.ToTable("chunks");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.SectionPath).HasMaxLength(1000);
        builder.Property(c => c.EmbeddingModel).HasMaxLength(100);

        builder.Property(c => c.Embedding)
            .HasColumnType($"vector({EmbeddingDimensions})");

        builder.Property(c => c.CreatedAt).HasDefaultValueSql("now()");

        // Turkish stemming ("raporları" → "rapor") and stop words. I and İ are mapped to ı
        // and i first: without that, lower-casing turns "IŞIK" into "işik", which matches
        // nothing. The context header is part of Content, so "which document, which section"
        // is searchable too. Generated and stored: PostgreSQL keeps it in step with Content.
        builder.Property(c => c.SearchVector)
            .HasComputedColumnSql($"to_tsvector('{SearchConfiguration}'::regconfig, {TurkishCaseFoldSql("content")})", stored: true);

        builder.HasIndex(c => c.SearchVector).HasMethod("GIN");

        // One chunk per position; also serves lookups by document.
        builder.HasIndex(c => new { c.DocumentId, c.Ordinal }).IsUnique();
    }
}
