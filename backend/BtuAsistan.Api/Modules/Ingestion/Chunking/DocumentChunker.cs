using System.Globalization;
using System.Text;
using BtuAsistan.Api.Modules.Ingestion.Parsing;

namespace BtuAsistan.Api.Modules.Ingestion.Chunking;

// Adapted from DocAssistant. Differences: the document's name is always the title staff
// entered at upload (never replaced by a heading in the file), and the header carries the
// validity period, e.g. "Belge: Akademik Takvim (2026-2027) > Güz Dönemi".
//
// Turns a parsed document into chunks (PLANNING P5). The one chunking algorithm for every
// file format: it sees only blocks, never the file (P3).
//
// Headings open a section and close the chunk being built, so a chunk never spans two
// sections. Inside a section, paragraphs and table rows are added one by one until the
// next one would push the text over MaxTextLength. A paragraph or row that is too long on
// its own is split into sentences, and a sentence that is still too long into words.
public static class DocumentChunker
{
    // Characters of text per chunk, not counting the context header. About 350–400 tokens of
    // Turkish; one article of a regulation usually fits.
    public const int MaxTextLength = 1500;

    private const string HeaderPrefix = "Belge: ";

    // Between a chunk's context header and its text. The header is one line, so the first
    // occurrence ends it.
    private const string HeaderSeparator = "\n\n";

    // A chunk's text without its context header: what the document itself says, for
    // showing a source to a reader. The header is for search and the model.
    public static string WithoutContextHeader(string content)
    {
        var end = content.IndexOf(HeaderSeparator, StringComparison.Ordinal);

        return content.StartsWith(HeaderPrefix, StringComparison.Ordinal) && end >= 0
            ? content[(end + HeaderSeparator.Length)..]
            : content;
    }

    // Compares titles the way a reader would: case-insensitive, with Turkish i/İ and ı/I.
    private static readonly StringComparer TitleComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);

    public static IReadOnlyList<ChunkDraft> Chunk(string documentTitle, string? validityPeriod, ParsedDocument document)
    {
        var blocks = document.Blocks;

        // A document that opens with its only level-1 heading is naming itself ("BTÜ İMEP
        // YÖNERGESİ"); that heading is not a section and would only lengthen every path.
        if (OpensWithTitleHeading(blocks))
        {
            blocks = blocks.Skip(1).ToList();
        }

        var name = string.IsNullOrWhiteSpace(validityPeriod)
            ? documentTitle.Trim()
            : $"{documentTitle.Trim()} ({validityPeriod.Trim()})";

        var builder = new ChunkBuilder(name, documentTitle.Trim());

        foreach (var block in blocks)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    builder.StartSection(heading.Text, heading.Level);
                    break;

                case ParagraphBlock paragraph:
                    builder.AddText(paragraph.Text, "\n\n", paragraph.PageNumber);
                    break;

                case TableBlock table:
                    var separator = "\n\n";
                    foreach (var row in FormatRows(table))
                    {
                        builder.AddText(row, separator, table.PageNumber);
                        separator = "\n";
                    }

                    break;
            }
        }

        builder.Flush();

        return builder.Chunks;
    }

    // With more than one level-1 heading they are the main sections ("Bölüm 1", "Bölüm 2"),
    // and the first of them must stay in the section path.
    private static bool OpensWithTitleHeading(IReadOnlyList<DocumentBlock> blocks) =>
        blocks.Count > 0
        && blocks[0] is HeadingBlock { Level: 1 } first
        && first.Text.Trim().Length > 0
        && blocks.OfType<HeadingBlock>().Count(heading => heading.Level == 1) == 1;

    // One line per data row, "Sütun: değer; Sütun: değer", so each row can be understood
    // without the header row. Empty cells are left out.
    private static IEnumerable<string> FormatRows(TableBlock table)
    {
        var header = table.HasHeaderRow && table.Rows.Count > 0 ? table.Rows[0] : null;
        var dataRows = header is null ? table.Rows : table.Rows.Skip(1);

        foreach (var row in dataRows)
        {
            var cells = new List<string>();

            for (var column = 0; column < row.Count; column++)
            {
                var value = row[column].Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                var name = header is not null && column < header.Count ? header[column].Trim() : "";
                cells.Add(name.Length > 0 ? $"{name}: {value}" : value);
            }

            if (cells.Count > 0)
            {
                yield return string.Join("; ", cells);
            }
        }
    }

    // Pieces of at most MaxTextLength characters: the text itself when it fits, otherwise
    // its sentences, otherwise words, and as a last resort fixed-length slices.
    private static IEnumerable<string> SplitToFit(string text)
    {
        if (text.Length <= MaxTextLength)
        {
            yield return text;
            yield break;
        }

        foreach (var sentence in TurkishSentenceSplitter.Split(text))
        {
            if (sentence.Length <= MaxTextLength)
            {
                yield return sentence;
                continue;
            }

            foreach (var piece in SplitWords(sentence))
            {
                yield return piece;
            }
        }
    }

    private static IEnumerable<string> SplitWords(string sentence)
    {
        var piece = new StringBuilder();

        foreach (var word in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (piece.Length > 0 && piece.Length + 1 + word.Length > MaxTextLength)
            {
                yield return piece.ToString();
                piece.Clear();
            }

            // A "word" longer than the limit (a long URL, a run of symbols) is cut up.
            var rest = word;
            while (rest.Length > MaxTextLength)
            {
                if (piece.Length > 0)
                {
                    yield return piece.ToString();
                    piece.Clear();
                }

                yield return rest[..MaxTextLength];
                rest = rest[MaxTextLength..];
            }

            if (piece.Length > 0)
            {
                piece.Append(' ');
            }

            piece.Append(rest);
        }

        if (piece.Length > 0)
        {
            yield return piece.ToString();
        }
    }

    // name: the title with the validity period, as shown in headers.
    // title: the bare title, for recognizing headings that only repeat it.
    private sealed class ChunkBuilder(string name, string title)
    {
        private readonly List<(int Level, string Text)> _headings = [];
        private readonly StringBuilder _text = new();
        private int? _pageNumber;

        public List<ChunkDraft> Chunks { get; } = [];

        // Null when no heading is open; the header then has only the name.
        private string? SectionPath =>
            _headings.Count == 0 ? null : string.Join(" > ", _headings.Select(h => h.Text));

        public void StartSection(string text, int level)
        {
            Flush();

            // A heading closes every open heading at its own level or deeper.
            while (_headings.Count > 0 && _headings[^1].Level >= level)
            {
                _headings.RemoveAt(_headings.Count - 1);
            }

            // Repeating the title after "Belge: <title>" in every header adds nothing.
            var trimmed = text.Trim();
            if (trimmed.Length > 0 && !TitleComparer.Equals(trimmed, title))
            {
                _headings.Add((level, trimmed));
            }
        }

        // separator goes before the text unless it opens a chunk; the pieces of a text
        // that had to be split are joined with spaces.
        public void AddText(string text, string separator, int? pageNumber)
        {
            var trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return;
            }

            foreach (var piece in SplitToFit(trimmed))
            {
                Add(piece, separator, pageNumber);
                separator = " ";
            }
        }

        public void Flush()
        {
            if (_text.Length == 0)
            {
                return;
            }

            var sectionPath = SectionPath;
            var header = sectionPath is null
                ? $"{HeaderPrefix}{name}"
                : $"{HeaderPrefix}{name} > {sectionPath}";

            Chunks.Add(new ChunkDraft(Chunks.Count, $"{header}{HeaderSeparator}{_text}", _pageNumber, sectionPath));

            _text.Clear();
            _pageNumber = null;
        }

        private void Add(string piece, string separator, int? pageNumber)
        {
            if (_text.Length > 0 && _text.Length + separator.Length + piece.Length > MaxTextLength)
            {
                Flush();
            }

            if (_text.Length == 0)
            {
                // A chunk's page is the page of its first block.
                _pageNumber = pageNumber;
            }
            else
            {
                _text.Append(separator);
            }

            _text.Append(piece);
        }
    }
}
