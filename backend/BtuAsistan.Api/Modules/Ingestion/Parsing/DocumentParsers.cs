namespace BtuAsistan.Api.Modules.Ingestion.Parsing;

// Picks the reader for a stored document by the content type set at upload.
public sealed class DocumentParsers(IEnumerable<IDocumentParser> parsers)
{
    private readonly Dictionary<string, IDocumentParser> _byContentType =
        parsers.ToDictionary(p => p.ContentType, StringComparer.OrdinalIgnoreCase);

    public ParsedDocument Parse(string contentType, byte[] content) =>
        _byContentType.TryGetValue(contentType, out var parser)
            ? parser.Parse(content)
            : throw new DocumentParseException("Bu dosya türü okunamıyor.");
}
