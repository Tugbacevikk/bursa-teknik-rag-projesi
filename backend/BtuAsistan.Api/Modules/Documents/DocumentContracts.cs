using Microsoft.AspNetCore.Mvc;

namespace BtuAsistan.Api.Modules.Documents;

// multipart/form-data body of POST /api/documents.
public sealed class UploadDocumentForm
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    [FromForm(Name = "title")]
    public string? Title { get; init; }

    [FromForm(Name = "unit")]
    public string? Unit { get; init; }

    // A DocumentKind name, e.g. "Regulation".
    [FromForm(Name = "kind")]
    public string? Kind { get; init; }

    [FromForm(Name = "sourceUrl")]
    public string? SourceUrl { get; init; }

    [FromForm(Name = "validityPeriod")]
    public string? ValidityPeriod { get; init; }

    /// <summary>Checks the fields; the file is inspected separately. Messages are shown to staff as is.</summary>
    public Dictionary<string, string[]> Validate(out DocumentKind kind)
    {
        var errors = DocumentMetadata.Validate(Title, Unit, Kind, SourceUrl, ValidityPeriod, out kind);

        if (File is null)
        {
            errors["file"] = [DocumentEndpoints.FileRequiredMessage];
        }

        return errors;
    }
}

// The rules for a document's descriptive fields, shared by the upload endpoint and the
// seed import so both accept exactly the same values.
public static class DocumentMetadata
{
    public static Dictionary<string, string[]> Validate(
        string? title, string? unit, string? kindName, string? sourceUrl, string? validityPeriod, out DocumentKind kind)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > DocumentConfiguration.TitleMaxLength)
        {
            errors["title"] = [$"Başlık zorunludur ve en fazla {DocumentConfiguration.TitleMaxLength} karakter olabilir."];
        }

        if (string.IsNullOrWhiteSpace(unit) || unit.Trim().Length > DocumentConfiguration.UnitMaxLength)
        {
            errors["unit"] = [$"Birim zorunludur ve en fazla {DocumentConfiguration.UnitMaxLength} karakter olabilir."];
        }

        // Enum.TryParse would also accept numbers ("3"); only names are valid here.
        if (!Enum.TryParse(kindName, ignoreCase: true, out kind) || !Enum.IsDefined(kind) || int.TryParse(kindName, out _))
        {
            errors["kind"] = [$"Belge türü şunlardan biri olmalı: {string.Join(", ", Enum.GetNames<DocumentKind>())}."];
        }

        if (!string.IsNullOrWhiteSpace(sourceUrl)
            && (sourceUrl.Length > DocumentConfiguration.SourceUrlMaxLength
                || !Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            errors["sourceUrl"] = ["Kaynak adresi http:// ya da https:// ile başlayan tam bir adres olmalı."];
        }

        if (validityPeriod?.Trim().Length > DocumentConfiguration.ValidityPeriodMaxLength)
        {
            errors["validityPeriod"] = [$"Geçerlilik dönemi en fazla {DocumentConfiguration.ValidityPeriodMaxLength} karakter olabilir."];
        }

        return errors;
    }
}

public sealed record DocumentCreatedResponse(Guid Id, string Status, int Version);

// 409 body: the document that already has this exact file.
public sealed record DuplicateDocumentResponse(string Error, Guid ExistingDocumentId, string ExistingTitle);

// multipart/form-data body of PUT /api/documents/{id}/file.
public sealed class ReplaceDocumentFileForm
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }
}

public sealed record DocumentSummaryResponse(
    Guid Id,
    string Title,
    string Unit,
    string Kind,
    string Status,
    int Version,
    string FileName,
    long SizeBytes,
    int ChunkCount,
    string? FailureReason,
    DateTimeOffset UpdatedAt);

public sealed record DocumentDetailResponse(
    Guid Id,
    string Title,
    string Unit,
    string Kind,
    string? SourceUrl,
    string? ValidityPeriod,
    string Status,
    string? FailureReason,
    int AttemptCount,
    int Version,
    string FileName,
    string ContentType,
    long SizeBytes,
    int ChunkCount,
    string UploadedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ProcessedAt);
