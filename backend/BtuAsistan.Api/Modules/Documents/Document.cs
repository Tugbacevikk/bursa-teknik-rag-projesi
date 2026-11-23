namespace BtuAsistan.Api.Modules.Documents;

public class Document
{
    public Guid Id { get; set; }

    public required string Title { get; set; }

    // Office the document belongs to, e.g. "İMEP Koordinatörlüğü", "Öğrenci İşleri".
    public required string Unit { get; set; }

    public DocumentKind Kind { get; set; }

    // Where the official copy lives; shown with answers so students can check the source.
    public string? SourceUrl { get; set; }

    // The period the content applies to, e.g. "2026-2027"; keeps old and new rules apart.
    public string? ValidityPeriod { get; set; }

    // As uploaded; never used to build a path.
    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    // SHA-256 of the file, lower-case hex; the same file cannot be uploaded twice.
    public required string ContentHash { get; set; }

    // Starts at 1 and grows when a new file replaces the old one; the old file is not kept.
    public int Version { get; set; } = 1;

    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;

    // In words that can be shown to the uploader: why processing failed (Failed), or why
    // it was postponed (Pending, e.g. the AI service was unavailable). Cleared on Done.
    public string? FailureReason { get; set; }

    // Raised at the start of every processing attempt; processing stops after the limit.
    public int AttemptCount { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public Guid UploadedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DocumentFile? File { get; set; }

    public List<Chunk> Chunks { get; set; } = [];
}
