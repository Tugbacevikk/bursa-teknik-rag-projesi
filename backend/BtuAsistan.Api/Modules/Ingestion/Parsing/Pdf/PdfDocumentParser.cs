using BtuAsistan.Api.Modules.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace BtuAsistan.Api.Modules.Ingestion.Parsing.Pdf;

// Adapted from DocAssistant's PDF reader.
public sealed class PdfDocumentParser : IDocumentParser
{
    public const string UnreadableFileMessage =
        "Dosya okunamadı. Dosya bozuk olabilir ya da geçerli bir PDF değil.";

    public const string PasswordProtectedMessage =
        "Belge parola korumalı. Parolayı kaldırıp yeniden yükleyin.";

    public const string NoPagesMessage = "Belgede sayfa yok.";

    public const string NoTextMessage =
        "Belgede okunabilir metin bulunamadı. Belge taranmış bir görüntü olabilir; taranmış belgeler henüz desteklenmiyor.";

    // Text at least this much larger than the body font counts as a heading (14 pt over
    // a 12 pt body qualifies; 12.5 pt does not).
    private const double HeadingFontSizeFactor = 1.1;

    // Longer large-print text is a pull quote or a cover page, not a heading.
    private const int HeadingMaxLength = 200;

    public string ContentType => DocumentFileInspector.PdfContentType;

    public ParsedDocument Parse(byte[] content)
    {
        try
        {
            return ParseCore(content);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new DocumentParseException(PasswordProtectedMessage, ex);
        }
        catch (Exception ex) when (ex is not DocumentParseException)
        {
            // PdfPig reports malformed files with several exception types; none of their
            // messages are meant for end users.
            throw new DocumentParseException(UnreadableFileMessage, ex);
        }
    }

    private static ParsedDocument ParseCore(byte[] content)
    {
        using var document = PdfDocument.Open(content);

        if (document.NumberOfPages == 0)
        {
            throw new DocumentParseException(NoPagesMessage);
        }

        var lines = document.GetPages().SelectMany(PdfTextLayout.ReadLines).ToList();

        // Pages without any text are scans (images of text); OCR is out of scope
        // (PLANNING P4). Failing loudly beats reporting "done, 0 chunks".
        if (lines.Count == 0)
        {
            throw new DocumentParseException(NoTextMessage);
        }

        return new ParsedDocument(ToBlocks(PdfTextLayout.BuildParagraphs(lines)));
    }

    // A PDF does not mark headings, so they are guessed from font size: short text written
    // noticeably larger than the body is a heading, and the larger the font, the higher the
    // level. A wrong guess only costs a section name; the text itself is kept either way.
    private static List<DocumentBlock> ToBlocks(IReadOnlyList<PdfParagraph> paragraphs)
    {
        // The size most of the document's text is written in.
        var bodyFontSize = paragraphs
            .GroupBy(paragraph => paragraph.FontSize)
            .OrderByDescending(group => group.Sum(paragraph => paragraph.Text.Length))
            .First()
            .Key;

        bool IsHeading(PdfParagraph paragraph) =>
            paragraph.FontSize >= bodyFontSize * HeadingFontSizeFactor
            && paragraph.Text.Length <= HeadingMaxLength;

        // Largest size is level 1, the next one level 2, and so on.
        var headingSizes = paragraphs
            .Where(IsHeading)
            .Select(paragraph => paragraph.FontSize)
            .Distinct()
            .OrderByDescending(size => size)
            .ToList();

        var blocks = new List<DocumentBlock>(paragraphs.Count);

        foreach (var paragraph in paragraphs)
        {
            if (IsHeading(paragraph))
            {
                var level = headingSizes.IndexOf(paragraph.FontSize) + 1;
                blocks.Add(new HeadingBlock(paragraph.Text, level, paragraph.PageNumber));
            }
            else
            {
                blocks.Add(new ParagraphBlock(paragraph.Text, paragraph.PageNumber));
            }
        }

        return blocks;
    }
}
