using System.Text;
using UglyToad.PdfPig.Content;

namespace BtuAsistan.Api.Modules.Ingestion.Parsing.Pdf;

// Adapted from DocAssistant.
//
// A PDF stores letters and their positions, not lines or paragraphs. This rebuilds both
// from geometry: words on the same baseline form a line; lines
// with the same font size and normal line spacing form a paragraph.
internal static class PdfTextLayout
{
    // Lines further apart than the normal spacing by this factor start a new paragraph.
    private const double ParagraphGapFactor = 1.25;

    // Single spacing is about 1.2 × the font size, 1.5 spacing about 1.7 ×. A "normal"
    // spacing from this factor up may really be the gap between one-line paragraphs.
    private const double LooseSpacingFactor = 1.5;

    // Words whose baselines differ by less than this share of the font size are on one line.
    private const double SameLineTolerance = 0.3;

    public static List<PdfLine> ReadLines(Page page)
    {
        var words = page.GetWords()
            .Where(word => !string.IsNullOrWhiteSpace(TextOf(word)) && word.Letters.Count > 0)
            .OrderByDescending(Baseline)
            .ToList();

        var lines = new List<PdfLine>();
        var current = new List<Word>();

        foreach (var word in words)
        {
            if (current.Count > 0
                && Math.Abs(Baseline(current[0]) - Baseline(word)) > SameLineTolerance * word.Letters[0].PointSize)
            {
                lines.Add(ToLine(current, page.Number));
                current = [];
            }

            current.Add(word);
        }

        if (current.Count > 0)
        {
            lines.Add(ToLine(current, page.Number));
        }

        return lines;
    }

    public static List<PdfParagraph> BuildParagraphs(IReadOnlyList<PdfLine> lines)
    {
        var normalSpacing = FindNormalLineSpacing(lines);

        var paragraphs = new List<PdfParagraph>();
        var current = new List<PdfLine>();

        foreach (var line in lines)
        {
            if (current.Count > 0 && StartsNewParagraph(current[^1], line, normalSpacing))
            {
                paragraphs.Add(ToParagraph(current));
                current = [];
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            paragraphs.Add(ToParagraph(current));
        }

        return paragraphs;
    }

    private static bool StartsNewParagraph(PdfLine previous, PdfLine line, Dictionary<double, double> normalSpacing)
    {
        // A paragraph that continues on the next page is not rejoined: each block
        // carries a single page number.
        if (line.PageNumber != previous.PageNumber || line.FontSize != previous.FontSize)
        {
            return true;
        }

        var gap = previous.Baseline - line.Baseline;

        if (!normalSpacing.TryGetValue(line.FontSize, out var normal) || gap > normal * ParagraphGapFactor)
        {
            return true;
        }

        // Loose spacing looks the same for a double-spaced paragraph and for a list of
        // one-line paragraphs, so the text decides: a line that ends a sentence, followed
        // by one that does not continue in lower case, closes the paragraph.
        return normal >= line.FontSize * LooseSpacingFactor
            && EndsSentence(previous.Text)
            && !char.IsLower(line.Text[0]);
    }

    private static bool EndsSentence(string text)
    {
        var end = text.Length - 1;
        while (end > 0 && text[end] is ')' or ']' or '"' or '\'' or '”' or '’' or '»')
        {
            end--;
        }

        return text[end] is '.' or '!' or '?' or '…' or ':';
    }

    // Line spacing differs per document (single, 1.5, double), so it is measured instead
    // of assumed: for each font size, the smallest distance between two consecutive lines.
    private static Dictionary<double, double> FindNormalLineSpacing(IReadOnlyList<PdfLine> lines)
    {
        var spacing = new Dictionary<double, double>();

        for (var i = 1; i < lines.Count; i++)
        {
            var previous = lines[i - 1];
            var line = lines[i];

            if (line.PageNumber != previous.PageNumber || line.FontSize != previous.FontSize)
            {
                continue;
            }

            var gap = previous.Baseline - line.Baseline;

            if (gap > 0 && (!spacing.TryGetValue(line.FontSize, out var smallest) || gap < smallest))
            {
                spacing[line.FontSize] = gap;
            }
        }

        return spacing;
    }

    private static PdfLine ToLine(List<Word> words, int pageNumber)
    {
        var ordered = words.OrderBy(word => word.BoundingBox.Left).ToList();

        // The size most of the line is written in; a stray superscript does not change it.
        var fontSize = ordered
            .SelectMany(word => word.Letters)
            .GroupBy(letter => Math.Round(letter.PointSize, 1))
            .OrderByDescending(group => group.Count())
            .First()
            .Key;

        return new PdfLine(
            pageNumber,
            Baseline(ordered[0]),
            fontSize,
            string.Join(' ', ordered.Select(TextOf)));
    }

    // A word's text without control characters. A font that does not say which letter a
    // glyph is (common for symbols, such as Greek letters in LaTeX documents) can come
    // out as one, NUL included, which PostgreSQL cannot store in text.
    // The symbol is lost either way; dropping it keeps the rest of the text.
    private static string TextOf(Word word)
    {
        var text = word.Text;

        if (!text.Any(char.IsControl))
        {
            return text;
        }

        return string.Concat(text.Where(letter => !char.IsControl(letter)));
    }

    private static PdfParagraph ToParagraph(List<PdfLine> lines)
    {
        var text = new StringBuilder(lines[0].Text);

        for (var i = 1; i < lines.Count; i++)
        {
            var next = lines[i].Text;

            if (EndsWithLineBreakHyphen(text) && char.IsLower(next[0]))
            {
                // "yönet-" + "melik" → "yönetmelik"
                text.Length--;
            }
            else
            {
                text.Append(' ');
            }

            text.Append(next);
        }

        return new PdfParagraph(lines[0].PageNumber, lines[0].FontSize, text.ToString());
    }

    private static bool EndsWithLineBreakHyphen(StringBuilder text)
    {
        // Hyphen-minus, soft hyphen, hyphen. A dash (–) is punctuation and stays.
        return text.Length > 1
            && text[^1] is '-' or '­' or '‐'
            && char.IsLetter(text[^2]);
    }

    private static double Baseline(Word word) => word.Letters[0].StartBaseLine.Y;
}

internal sealed record PdfLine(int PageNumber, double Baseline, double FontSize, string Text);

internal sealed record PdfParagraph(int PageNumber, double FontSize, string Text);
