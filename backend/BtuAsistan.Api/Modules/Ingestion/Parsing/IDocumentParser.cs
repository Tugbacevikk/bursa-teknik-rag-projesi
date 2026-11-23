namespace BtuAsistan.Api.Modules.Ingestion.Parsing;

// One implementation per file format (PLANNING P2, P3).
public interface IDocumentParser
{
    // The content type DocumentFileInspector assigns to files of this format.
    string ContentType { get; }

    // Returns the blocks in reading order.
    //
    // Throws DocumentParseException when the file cannot be turned into text: corrupted,
    // password-protected, empty, or a scan without extractable text (PLANNING P4).
    ParsedDocument Parse(byte[] content);
}
