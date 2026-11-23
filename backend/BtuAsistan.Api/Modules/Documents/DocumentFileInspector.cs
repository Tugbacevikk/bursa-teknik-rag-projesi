using System.IO.Compression;
using System.Text;

namespace BtuAsistan.Api.Modules.Documents;

/// <summary>
/// Decides what an uploaded file really is from its bytes, not from its name or the
/// content type the browser claims: both are chosen by the uploader.
/// </summary>
public static class DocumentFileInspector
{
    public const long MaxSizeBytes = 20 * 1024 * 1024;

    public const string PdfContentType = "application/pdf";
    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string MarkdownContentType = "text/markdown";

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    // Legacy .doc files and password-protected Word files are "compound files", not zips.
    private static readonly byte[] CompoundFileSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    // Plain text has no signature, so for it alone the extension is the deciding hint.
    private static readonly string[] TextExtensions = [".md", ".markdown", ".txt"];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static readonly string TooLargeMessage = $"Dosya {MaxSizeBytes / (1024 * 1024)} MB'tan büyük.";

    /// <returns>
    /// The content type to store, or null with a reason shown to the uploader as is (so it is
    /// in Turkish, like the readers' messages).
    /// </returns>
    public static (string? ContentType, string? Error) Inspect(ReadOnlySpan<byte> content, string fileName)
    {
        if (content.IsEmpty)
        {
            return (null, "Dosya boş.");
        }

        if (content.Length > MaxSizeBytes)
        {
            return (null, TooLargeMessage);
        }

        if (content.StartsWith(PdfSignature))
        {
            return (PdfContentType, null);
        }

        if (content.StartsWith(ZipSignature))
        {
            return IsDocx(content)
                ? (DocxContentType, null)
                : (null, "Dosya bir ZIP arşivi ama Word (DOCX) belgesi değil.");
        }

        if (content.StartsWith(CompoundFileSignature))
        {
            return (null,
                "Belge eski Word biçiminde (.doc) ya da parola korumalı. Parolayı kaldırıp DOCX olarak kaydederek yeniden yükleyin.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (TextExtensions.Contains(extension))
        {
            return IsUtf8Text(content)
                ? (MarkdownContentType, null)
                : (null, "Metin dosyası UTF-8 biçiminde değil. Dosyayı UTF-8 olarak kaydedip yeniden yükleyin.");
        }

        return (null, "Yalnızca PDF, DOCX ve Markdown/TXT dosyaları kabul edilir.");
    }

    // A DOCX is a ZIP that holds word/document.xml. Only the entry list is read here; the
    // contents are read (with size limits) by the DOCX reader.
    private static bool IsDocx(ReadOnlySpan<byte> content)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content.ToArray()), ZipArchiveMode.Read);
            return archive.GetEntry("word/document.xml") is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    // NUL bytes do not occur in text; their presence means a binary file renamed to .txt.
    private static bool IsUtf8Text(ReadOnlySpan<byte> content)
    {
        if (content.Contains((byte)0))
        {
            return false;
        }

        try
        {
            StrictUtf8.GetCharCount(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
