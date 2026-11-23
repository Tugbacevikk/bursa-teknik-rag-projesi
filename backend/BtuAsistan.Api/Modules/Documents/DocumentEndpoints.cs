using System.Security.Claims;
using System.Security.Cryptography;
using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Audit;
using BtuAsistan.Api.Modules.Identity;
using BtuAsistan.Api.Modules.Ingestion;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BtuAsistan.Api.Modules.Documents;

public static class DocumentEndpoints
{
    // The file plus the other form fields.
    private const long MaxRequestBytes = DocumentFileInspector.MaxSizeBytes + 64 * 1024;

    public const string FileRequiredMessage = "Dosya seçilmedi.";

    private const string DuplicateError = "Bu dosya daha önce yüklenmiş.";

    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents").RequireAuthorization(Policies.Staff);

        // Form posts are protected by the X-Requested-With check (SessionCookieCsrfProtection),
        // not by ASP.NET Core's antiforgery tokens, which minimal APIs would otherwise demand.
        group.MapPost("/", UploadAsync)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes));
        group.MapPut("/{id:guid}/file", ReplaceFileAsync)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes));

        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/{id:guid}/file", DownloadAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);

        return app;
    }

    // Stores the document as Pending and returns at once; reading, chunking and embedding
    // happen in the background.
    private static async Task<Results<Created<DocumentCreatedResponse>, ValidationProblem, Conflict<DuplicateDocumentResponse>>> UploadAsync(
        [FromForm] UploadDocumentForm form,
        ClaimsPrincipal principal,
        AppDbContext db,
        IIngestionQueue queue,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var errors = form.Validate(out var kind);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var file = await ReadFileAsync(form.File!, cancellationToken);
        if (file.Error is not null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [file.Error] });
        }

        if (await FindByHashAsync(db, file.Hash, cancellationToken) is { } existing)
        {
            return TypedResults.Conflict(new DuplicateDocumentResponse(DuplicateError, existing.Id, existing.Title));
        }

        var now = timeProvider.GetUtcNow();
        var userId = principal.GetUserId();
        var document = new Document
        {
            Id = Guid.CreateVersion7(),
            Title = form.Title!.Trim(),
            Unit = form.Unit!.Trim(),
            Kind = kind,
            SourceUrl = NullIfBlank(form.SourceUrl),
            ValidityPeriod = NullIfBlank(form.ValidityPeriod),
            FileName = SafeFileName(form.File!.FileName),
            ContentType = file.ContentType!,
            SizeBytes = file.Content.Length,
            ContentHash = file.Hash,
            UploadedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
            File = new DocumentFile { Content = file.Content },
        };

        db.Documents.Add(document);
        db.AddAuditEntry(userId, AuditActions.DocumentUploaded, AuditEntityTypes.Document, document.Id,
            new { document.Title, document.FileName, document.Version });

        if (!await TrySaveAsync(db, cancellationToken))
        {
            var winner = await FindByHashAsync(db, file.Hash, cancellationToken);
            return TypedResults.Conflict(new DuplicateDocumentResponse(DuplicateError, winner!.Id, winner.Title));
        }

        // If this fails the document is still Pending and the recovery sweep queues it.
        await queue.EnqueueAsync(document.Id, cancellationToken);

        return TypedResults.Created(
            $"/api/documents/{document.Id}",
            new DocumentCreatedResponse(document.Id, document.Status.ToString(), document.Version));
    }

    // A new file for an existing document (e.g. next year's calendar). The old file and its
    // chunks are removed at once, so search never mixes two versions of the same document.
    private static async Task<Results<Ok<DocumentCreatedResponse>, NotFound, ValidationProblem, Conflict<DuplicateDocumentResponse>>> ReplaceFileAsync(
        Guid id,
        [FromForm] ReplaceDocumentFileForm form,
        ClaimsPrincipal principal,
        AppDbContext db,
        IIngestionQueue queue,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (form.File is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [FileRequiredMessage] });
        }

        // Read and checked before the transaction: no row stays locked during the upload.
        var file = await ReadFileAsync(form.File, cancellationToken);
        if (file.Error is not null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [file.Error] });
        }

        // Deleting the chunks runs as its own statement; the transaction ties it to the save
        // below, so a failed save leaves the old chunks in place.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The background worker may be saving chunks of the current version right now. The
        // row lock makes the two take turns: either its chunks land first and are deleted
        // below, or this commits first and the worker sees a new version and discards its work.
        var locked = await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM documents WHERE id = {id} FOR UPDATE")
            .ToListAsync(cancellationToken);
        if (locked.Count == 0)
        {
            return TypedResults.NotFound();
        }

        var document = await db.Documents
            .Include(d => d.File)
            .SingleAsync(d => d.Id == id, cancellationToken);

        // Also catches re-uploading the current file of this same document.
        if (await FindByHashAsync(db, file.Hash, cancellationToken) is { } existing)
        {
            return TypedResults.Conflict(new DuplicateDocumentResponse(DuplicateError, existing.Id, existing.Title));
        }

        var previousVersion = document.Version;
        await db.Chunks.Where(c => c.DocumentId == id).ExecuteDeleteAsync(cancellationToken);

        document.FileName = SafeFileName(form.File.FileName);
        document.ContentType = file.ContentType!;
        document.SizeBytes = file.Content.Length;
        document.ContentHash = file.Hash;
        document.File!.Content = file.Content;
        document.Version++;
        document.Status = DocumentStatus.Pending;
        document.FailureReason = null;
        document.AttemptCount = 0;
        document.ProcessedAt = null;
        document.UpdatedAt = timeProvider.GetUtcNow();

        var userId = principal.GetUserId();
        db.AddAuditEntry(userId, AuditActions.DocumentReplaced, AuditEntityTypes.Document, document.Id,
            new { document.Title, document.FileName, PreviousVersion = previousVersion, document.Version });

        if (!await TrySaveAsync(db, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            var winner = await FindByHashAsync(db, file.Hash, cancellationToken);
            return TypedResults.Conflict(new DuplicateDocumentResponse(DuplicateError, winner!.Id, winner.Title));
        }

        await transaction.CommitAsync(cancellationToken);

        // After the commit, so the worker never sees the old file. If this fails the
        // document is still Pending and the recovery sweep queues it.
        await queue.EnqueueAsync(document.Id, cancellationToken);

        return TypedResults.Ok(new DocumentCreatedResponse(document.Id, document.Status.ToString(), document.Version));
    }

    private static async Task<List<DocumentSummaryResponse>> ListAsync(AppDbContext db, CancellationToken cancellationToken) =>
        await db.Documents.AsNoTracking()
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => new DocumentSummaryResponse(
                d.Id,
                d.Title,
                d.Unit,
                d.Kind.ToString(),
                d.Status.ToString(),
                d.Version,
                d.FileName,
                d.SizeBytes,
                d.Chunks.Count,
                d.FailureReason,
                d.UpdatedAt))
            .ToListAsync(cancellationToken);

    private static async Task<Results<Ok<DocumentDetailResponse>, NotFound>> GetAsync(
        Guid id, AppDbContext db, CancellationToken cancellationToken)
    {
        var detail = await db.Documents.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new DocumentDetailResponse(
                d.Id,
                d.Title,
                d.Unit,
                d.Kind.ToString(),
                d.SourceUrl,
                d.ValidityPeriod,
                d.Status.ToString(),
                d.FailureReason,
                d.AttemptCount,
                d.Version,
                d.FileName,
                d.ContentType,
                d.SizeBytes,
                d.Chunks.Count,
                db.Users.Where(u => u.Id == d.UploadedByUserId).Select(u => u.Email).Single(),
                d.CreatedAt,
                d.UpdatedAt,
                d.ProcessedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
    }

    // Always as an attachment: the browser saves the file instead of opening it inside the
    // app's origin, where a crafted file could run script.
    private static async Task<Results<FileContentHttpResult, NotFound>> DownloadAsync(
        Guid id, AppDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var download = await db.Documents.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new { d.FileName, d.ContentType, d.File!.Content })
            .SingleOrDefaultAsync(cancellationToken);

        if (download is null)
        {
            return TypedResults.NotFound();
        }

        http.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(download.Content, download.ContentType, download.FileName);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id, ClaimsPrincipal principal, AppDbContext db, CancellationToken cancellationToken)
    {
        var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return TypedResults.NotFound();
        }

        // The file and chunks go with it (ON DELETE CASCADE in the database).
        db.Documents.Remove(document);
        db.AddAuditEntry(principal.GetUserId(), AuditActions.DocumentDeleted, AuditEntityTypes.Document, document.Id,
            new { document.Title, document.FileName, document.Version });
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<UploadedFile> ReadFileAsync(IFormFile formFile, CancellationToken cancellationToken)
    {
        if (formFile.Length > DocumentFileInspector.MaxSizeBytes)
        {
            return UploadedFile.Invalid(DocumentFileInspector.TooLargeMessage);
        }

        using var buffer = new MemoryStream((int)formFile.Length);
        await formFile.CopyToAsync(buffer, cancellationToken);
        var content = buffer.ToArray();

        var (contentType, error) = DocumentFileInspector.Inspect(content, formFile.FileName);
        if (error is not null)
        {
            return UploadedFile.Invalid(error);
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        return new UploadedFile(content, contentType, hash, null);
    }

    private static Task<Document?> FindByHashAsync(AppDbContext db, string hash, CancellationToken cancellationToken) =>
        db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.ContentHash == hash, cancellationToken);

    // Two uploads of the same file at the same moment both pass the lookup; the unique
    // index stops the second one. Returns false in that case.
    private static async Task<bool> TrySaveAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_documents_content_hash",
        })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    // Keeps only the name: some browsers send the full client path ("C:\Users\...\a.pdf").
    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/')).Trim();
        if (name.Length == 0)
        {
            name = "document";
        }

        return name.Length <= DocumentConfiguration.FileNameMaxLength
            ? name
            : name[..DocumentConfiguration.FileNameMaxLength];
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record UploadedFile(byte[] Content, string? ContentType, string Hash, string? Error)
    {
        public static UploadedFile Invalid(string error) => new([], null, string.Empty, error);
    }
}
