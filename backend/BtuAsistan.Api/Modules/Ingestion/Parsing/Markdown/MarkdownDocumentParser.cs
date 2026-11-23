using System.Text;
using System.Text.RegularExpressions;
using BtuAsistan.Api.Modules.Documents;

namespace BtuAsistan.Api.Modules.Ingestion.Parsing.Markdown;

// Reads Markdown and plain text. Plain text is Markdown without markup: its paragraphs
// come out as paragraphs. Covers what the school's documents use: headings (#, and
// underlined with === / ---), paragraphs, list items, pipe tables and code fences.
// Inline markup is removed so search sees words, not asterisks.
public sealed partial class MarkdownDocumentParser : IDocumentParser
{
    public const string NoTextMessage = "Belgede okunabilir metin bulunamadı.";

    public const string InvalidTextMessage = "Dosya geçerli bir UTF-8 metin dosyası değil.";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public string ContentType => DocumentFileInspector.MarkdownContentType;

    public ParsedDocument Parse(byte[] content)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException ex)
        {
            throw new DocumentParseException(InvalidTextMessage, ex);
        }

        var blocks = new Reader().Read(Normalize(text));

        // Failing loudly beats reporting "done, 0 chunks" (PLANNING P4).
        return blocks.Count == 0
            ? throw new DocumentParseException(NoTextMessage)
            : new ParsedDocument(blocks);
    }

    // One line ending, no byte order mark, and no control characters other than line
    // breaks and tabs: PostgreSQL cannot store NUL in text.
    private static string Normalize(string text)
    {
        text = text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');
        return text.Any(c => char.IsControl(c) && c is not '\n' and not '\t')
            ? string.Concat(text.Where(c => !char.IsControl(c) || c is '\n' or '\t'))
            : text;
    }

    // Line-by-line state machine. Kept separate so the parser itself stays stateless and
    // can be shared.
    private sealed class Reader
    {
        private readonly List<DocumentBlock> _blocks = [];
        private readonly List<string> _paragraph = [];
        private readonly List<IReadOnlyList<string>> _tableRows = [];
        private bool _tableHasHeader;
        private bool _inFence;

        public List<DocumentBlock> Read(string text)
        {
            foreach (var line in text.Split('\n'))
            {
                ReadLine(line);
            }

            FlushParagraph();
            FlushTable();
            return _blocks;
        }

        private void ReadLine(string line)
        {
            var trimmed = line.Trim();

            // Code blocks keep their text as one paragraph; markup inside is not interpreted.
            if (Fence().IsMatch(trimmed))
            {
                FlushParagraph();
                _inFence = !_inFence;
                return;
            }

            if (_inFence)
            {
                if (trimmed.Length > 0)
                {
                    _paragraph.Add(trimmed);
                }

                return;
            }

            if (trimmed.StartsWith('|'))
            {
                FlushParagraph();
                ReadTableRow(trimmed);
                return;
            }

            FlushTable();

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                return;
            }

            if (AtxHeading().Match(trimmed) is { Success: true } heading)
            {
                FlushParagraph();
                AddHeading(heading.Groups["text"].Value, heading.Groups["marks"].Length);
                return;
            }

            // "Title" followed by "=====" or "-----" is a heading when the title is one line.
            if (SetextUnderline().Match(trimmed) is { Success: true } underline && _paragraph.Count == 1)
            {
                var title = _paragraph[0];
                _paragraph.Clear();
                AddHeading(title, underline.Value[0] == '=' ? 1 : 2);
                return;
            }

            if (ThematicBreak().IsMatch(trimmed))
            {
                FlushParagraph();
                return;
            }

            // Each list item is its own paragraph; following lines without a marker continue it.
            if (ListItem().Match(trimmed) is { Success: true } item)
            {
                FlushParagraph();
                var marker = char.IsDigit(item.Groups["marker"].Value[0]) ? item.Groups["marker"].Value : "-";
                _paragraph.Add($"{marker} {item.Groups["text"].Value}");
                return;
            }

            _paragraph.Add(trimmed);
        }

        private void ReadTableRow(string line)
        {
            var cells = line.Trim('|').Split('|').Select(cell => CleanInline(cell.Trim())).ToList();

            // The "|---|:--:|" line under the first row marks that row as the header.
            if (cells.All(cell => TableSeparatorCell().IsMatch(cell)))
            {
                _tableHasHeader = _tableRows.Count == 1;
                return;
            }

            if (cells.Any(cell => cell.Length > 0))
            {
                _tableRows.Add(cells);
            }
        }

        private void AddHeading(string text, int level)
        {
            var clean = CleanInline(text);
            if (clean.Length > 0)
            {
                _blocks.Add(new HeadingBlock(clean, level, PageNumber: null));
            }
        }

        private void FlushParagraph()
        {
            if (_paragraph.Count == 0)
            {
                return;
            }

            var text = CleanInline(string.Join(' ', _paragraph));
            _paragraph.Clear();

            if (text.Length > 0)
            {
                _blocks.Add(new ParagraphBlock(text, PageNumber: null));
            }
        }

        private void FlushTable()
        {
            if (_tableRows.Count > 0)
            {
                _blocks.Add(new TableBlock(_tableRows.ToList(), _tableHasHeader, PageNumber: null));
            }

            _tableRows.Clear();
            _tableHasHeader = false;
        }
    }

    // [text](url) keeps both, since the address is often the answer ("where is the form?");
    // ![alt](url) keeps the description; **bold**, __bold__ and `code` lose their marks.
    private static string CleanInline(string text)
    {
        text = Image().Replace(text, "$1");
        text = Link().Replace(text, "$1 ($2)");
        text = text.Replace("**", string.Empty).Replace("__", string.Empty).Replace("`", string.Empty);
        text = EscapedCharacter().Replace(text, "$1");
        return Whitespace().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"^(```|~~~)")]
    private static partial Regex Fence();

    [GeneratedRegex(@"^(?<marks>#{1,6})\s+(?<text>.*?)\s*#*\s*$")]
    private static partial Regex AtxHeading();

    [GeneratedRegex(@"^(={2,}|-{2,})$")]
    private static partial Regex SetextUnderline();

    [GeneratedRegex(@"^([-*_])(\s*\1){2,}$")]
    private static partial Regex ThematicBreak();

    [GeneratedRegex(@"^(?<marker>[-*+]|\d{1,3}[.)])\s+(?<text>.+)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^:?-{3,}:?$")]
    private static partial Regex TableSeparatorCell();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)[^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\\([\\`*_{}\[\]()#+\-.!|])")]
    private static partial Regex EscapedCharacter();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
