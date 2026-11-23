using System.IO.Compression;
using System.Text;
using BtuAsistan.Api.Modules.Documents;
using BtuAsistan.Api.Modules.Ingestion.Parsing;
using BtuAsistan.Api.Modules.Ingestion.Parsing.Docx;
using BtuAsistan.Api.Modules.Ingestion.Parsing.Markdown;
using BtuAsistan.Api.Modules.Ingestion.Parsing.Pdf;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace BtuAsistan.Api.Tests.Unit;

public sealed class MarkdownParserTests
{
    private static ParsedDocument Parse(string text) => new MarkdownDocumentParser().Parse(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Reads_headings_paragraphs_and_lists()
    {
        var document = Parse("# Yönerge\n\n## Amaç\nBu yönerge\nİMEP'i düzenler.\n\n- Birinci şart\n- İkinci şart\n");

        Assert.Collection(document.Blocks,
            block => Assert.Equal(new HeadingBlock("Yönerge", 1, null), block),
            block => Assert.Equal(new HeadingBlock("Amaç", 2, null), block),
            block => Assert.Equal(new ParagraphBlock("Bu yönerge İMEP'i düzenler.", null), block),
            block => Assert.Equal(new ParagraphBlock("- Birinci şart", null), block),
            block => Assert.Equal(new ParagraphBlock("- İkinci şart", null), block));
    }

    [Fact]
    public void Reads_a_table_with_its_header_row()
    {
        var table = Assert.IsType<TableBlock>(Assert.Single(Parse("| Etkinlik | Tarih |\n|---|---|\n| **Başvuru** | 15 Temmuz |\n").Blocks));

        Assert.True(table.HasHeaderRow);
        Assert.Equal(["Başvuru", "15 Temmuz"], table.Rows[1]);
    }

    [Fact]
    public void Link_keeps_its_text_and_address()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Parse("Form için [buraya](https://btu.edu.tr/form.pdf) bakın.").Blocks));

        Assert.Contains("buraya", paragraph.Text);
        Assert.Contains("https://btu.edu.tr/form.pdf", paragraph.Text);
    }

    // PostgreSQL cannot store NUL in text (Faz 2 bug fix).
    [Fact]
    public void Control_characters_are_dropped()
    {
        var paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(Parse("Metin\0 içinde\u0007 denetim").Blocks));

        Assert.Equal("Metin içinde denetim", paragraph.Text);
    }

    [Fact]
    public void Byte_order_mark_and_windows_line_endings_are_handled()
    {
        var document = Parse("﻿# Başlık\r\nMetin\r\n");

        Assert.Equal(new HeadingBlock("Başlık", 1, null), document.Blocks[0]);
    }

    [Fact]
    public void Text_that_is_not_utf8_is_refused()
    {
        var error = Assert.Throws<DocumentParseException>(() => new MarkdownDocumentParser().Parse([0x49, 0xDD, 0xFE, 0x20]));

        Assert.Equal(MarkdownDocumentParser.InvalidTextMessage, error.Message);
    }

    [Fact]
    public void Empty_text_is_refused()
    {
        var error = Assert.Throws<DocumentParseException>(() => Parse("   \n\n  "));

        Assert.Equal(MarkdownDocumentParser.NoTextMessage, error.Message);
    }
}

public sealed class DocxParserTests
{
    [Fact]
    public void Headings_come_from_the_outline_level()
    {
        var document = new DocxDocumentParser().Parse(TestFiles.Docx(
            ("BTÜ İMEP Yönergesi", 0),
            ("Amaç", 1),
            ("Öğrenci işletmede on altı hafta çalışır.", null)));

        Assert.Collection(document.Blocks,
            block => Assert.Equal(new HeadingBlock("BTÜ İMEP Yönergesi", 1, null), block),
            block => Assert.Equal(new HeadingBlock("Amaç", 2, null), block),
            block => Assert.Equal(new ParagraphBlock("Öğrenci işletmede on altı hafta çalışır.", null), block));
    }

    [Fact]
    public void Not_a_word_file_is_refused()
    {
        var error = Assert.Throws<DocumentParseException>(() => new DocxDocumentParser().Parse(Encoding.UTF8.GetBytes("not a docx")));

        Assert.Equal(DocxDocumentParser.UnreadableFileMessage, error.Message);
    }

    [Fact]
    public void Document_without_text_is_refused()
    {
        var error = Assert.Throws<DocumentParseException>(() => new DocxDocumentParser().Parse(TestFiles.Docx()));

        Assert.Equal(DocxDocumentParser.NoTextMessage, error.Message);
    }
}

public sealed class PdfParserTests
{
    // Standard PDF fonts have no Turkish letters; the reading logic does not depend on them.
    [Fact]
    public void Larger_text_becomes_a_heading_and_pages_are_kept()
    {
        var document = new PdfDocumentParser().Parse(TestFiles.Pdf(
            [("Internship Rules", 18), ("Students work for sixteen weeks in a company.", 11), ("Each week they write a short report.", 11)],
            [("Grading", 18), ("The company mentor gives forty percent of the grade.", 11)]));

        var headings = document.Blocks.OfType<HeadingBlock>().ToList();
        Assert.Equal(["Internship Rules", "Grading"], headings.Select(h => h.Text));
        Assert.Equal([1, 2], headings.Select(h => h.PageNumber));

        var text = string.Join(" ", document.Blocks.OfType<ParagraphBlock>().Select(p => p.Text));
        Assert.Contains("sixteen weeks", text);
        Assert.Contains("forty percent", text);
        Assert.Contains(document.Blocks.OfType<ParagraphBlock>(), p => p.PageNumber == 2);
    }

    [Fact]
    public void Not_a_pdf_is_refused()
    {
        var error = Assert.Throws<DocumentParseException>(() => new PdfDocumentParser().Parse(Encoding.UTF8.GetBytes("%PDF-1.7 broken")));

        Assert.Equal(PdfDocumentParser.UnreadableFileMessage, error.Message);
    }
}

// What a file is, from its bytes (the name and the browser's claim are the uploader's).
public sealed class DocumentFileInspectorTests
{
    [Fact]
    public void Pdf_is_recognized_by_its_signature_whatever_the_name()
    {
        Assert.Equal((DocumentFileInspector.PdfContentType, null), DocumentFileInspector.Inspect(TestFiles.Pdf([("Text", 11)]), "yonerge.txt"));
    }

    [Fact]
    public void Docx_is_recognized()
    {
        Assert.Equal((DocumentFileInspector.DocxContentType, null), DocumentFileInspector.Inspect(TestFiles.Docx(("Metin", null)), "x.docx"));
    }

    [Theory]
    [InlineData("sss.md")]
    [InlineData("sss.markdown")]
    [InlineData("SSS.TXT")]
    public void Utf8_text_with_a_text_extension_is_markdown(string fileName)
    {
        Assert.Equal((DocumentFileInspector.MarkdownContentType, null), DocumentFileInspector.Inspect("İMEP soruları"u8, fileName));
    }

    [Fact]
    public void Zip_that_is_not_a_word_file_is_refused()
    {
        var (type, error) = DocumentFileInspector.Inspect(TestFiles.Zip("notes.txt"), "belge.docx");

        Assert.Null(type);
        Assert.Contains("ZIP", error);
    }

    [Fact]
    public void Legacy_or_protected_word_file_is_refused_with_advice()
    {
        var (type, error) = DocumentFileInspector.Inspect([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0], "eski.doc");

        Assert.Null(type);
        Assert.Contains("DOCX", error);
    }

    [Fact]
    public void Text_not_in_utf8_is_refused()
    {
        var (type, error) = DocumentFileInspector.Inspect([0x49, 0xDD, 0xFE], "sss.txt");

        Assert.Null(type);
        Assert.Contains("UTF-8", error);
    }

    [Theory]
    [InlineData("resim.png")]
    [InlineData("tablo.xlsx")]
    [InlineData("script.sh")]
    public void Other_files_are_refused(string fileName)
    {
        var (type, error) = DocumentFileInspector.Inspect("içerik"u8, fileName);

        Assert.Null(type);
        Assert.NotNull(error);
    }

    [Fact]
    public void Empty_and_too_large_files_are_refused()
    {
        Assert.Equal((null, "Dosya boş."), DocumentFileInspector.Inspect([], "a.pdf"));
        Assert.Equal(
            (null, DocumentFileInspector.TooLargeMessage),
            DocumentFileInspector.Inspect(new byte[DocumentFileInspector.MaxSizeBytes + 1], "a.pdf"));
    }
}

// Small documents made in code, so the tests carry no binary files.
internal static class TestFiles
{
    // Paragraphs with an outline level (0 = heading 1) or none.
    public static byte[] Docx(params (string Text, int? OutlineLevel)[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body();
            foreach (var (text, level) in paragraphs)
            {
                var paragraph = new Paragraph(new Run(new Text(text)));
                if (level is { } outline)
                {
                    paragraph.PrependChild(new ParagraphProperties(new OutlineLevel { Val = outline }));
                }

                body.AppendChild(paragraph);
            }

            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(body);
        }

        return stream.ToArray();
    }

    // One page per list; each line at its font size, top down.
    public static byte[] Pdf(params (string Text, double Size)[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            var y = 780.0;
            foreach (var (text, size) in lines)
            {
                page.AddText(text, size, new PdfPoint(60, y), font);
                y -= size * 1.6;
            }
        }

        return builder.Build();
    }

    public static byte[] Zip(string entry)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
            writer.Write("not a word file");
        }

        return stream.ToArray();
    }
}
