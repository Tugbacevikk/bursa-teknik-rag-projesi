namespace BtuAsistan.Api.Modules.Ingestion.Chunking;

// A chunk before it belongs to a stored document: the processor adds ids, version and the
// embedding when it turns drafts into Chunk rows.
//
// Content starts with the context header ("Belge: ... > ..."), followed by a blank line
// and the chunk's text (PLANNING P5).
public sealed record ChunkDraft(int Ordinal, string Content, int? PageNumber, string? SectionPath);
