namespace BtuAsistan.Api.Modules.Ingestion.Parsing;

// The common model every reader produces and the only thing chunking sees (PLANNING P3).
// Blocks are listed in reading order.
//
// PageNumber is 1-based and null when the format has no pages (DOCX, Markdown).
public abstract record DocumentBlock(int? PageNumber);

// Level 1 is the top-level heading; deeper sections have higher numbers.
public sealed record HeadingBlock(string Text, int Level, int? PageNumber) : DocumentBlock(PageNumber);

// Also used for list items, which keep their marker ("- ", "1. ").
public sealed record ParagraphBlock(string Text, int? PageNumber) : DocumentBlock(PageNumber);

// Rows[row][column] is the text of one cell. When HasHeaderRow is true, Rows[0] holds the
// column names.
public sealed record TableBlock(
    IReadOnlyList<IReadOnlyList<string>> Rows,
    bool HasHeaderRow,
    int? PageNumber) : DocumentBlock(PageNumber);

// What a reader returns. The document's title is not part of it: that is entered at
// upload time and stored on the document row.
public sealed record ParsedDocument(IReadOnlyList<DocumentBlock> Blocks);
