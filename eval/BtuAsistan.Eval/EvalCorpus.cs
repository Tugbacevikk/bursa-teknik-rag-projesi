using System.Security.Cryptography;
using System.Text.Json;
using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Documents;
using BtuAsistan.Api.Modules.Ingestion;
using BtuAsistan.Api.Modules.Ingestion.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BtuAsistan.Eval;

// eval/documents.json: the documents the questions are about. "id" is what questions refer
// to (sources[].document); "file" is relative to the eval folder.
public sealed record CorpusManifest(List<CorpusEntry> Documents);

public sealed record CorpusEntry(string Id, string File, string Title, string Unit, string Kind, string? ValidityPeriod);

public static class EvalCorpus
{
    public static CorpusManifest Load(string path)
    {
        var manifest = JsonSerializer.Deserialize<CorpusManifest>(File.ReadAllText(path), JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"{path} is empty.");

        var duplicates = manifest.Documents.GroupBy(d => d.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        return duplicates.Count == 0
            ? manifest
            : throw new InvalidOperationException($"Duplicate document ids in {path}: {string.Join(", ", duplicates)}");
    }

    /// <summary>
    /// Brings the evaluation database in line with the manifest, through the API's own
    /// processing code: unchanged files are kept (same content hash) and re-embedded if their
    /// vectors come from another model, changed or new ones are read, chunked and embedded,
    /// and documents no longer listed are removed.
    /// </summary>
    /// <returns>The database id of every manifest document that is ready, by manifest id.</returns>
    public static async Task<Dictionary<string, Guid>> SyncAsync(
        CorpusManifest manifest,
        string evalFolder,
        Guid uploaderId,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var ready = new Dictionary<string, Guid>();
        var keptHashes = new HashSet<string>();

        foreach (var entry in manifest.Documents)
        {
            var path = Path.GetFullPath(Path.Combine(evalFolder, entry.File));
            if (!File.Exists(path))
            {
                Console.WriteLine($"  ! {entry.Id}: file not found ({path})");
                continue;
            }

            var content = await File.ReadAllBytesAsync(path, cancellationToken);
            var hash = Convert.ToHexStringLower(SHA256.HashData(content));
            keptHashes.Add(hash);

            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var existing = await db.Documents.SingleOrDefaultAsync(d => d.ContentHash == hash, cancellationToken);
            if (existing is { Status: DocumentStatus.Done } && existing.Title == entry.Title
                && existing.ValidityPeriod == entry.ValidityPeriod)
            {
                // Unchanged, but its vectors may come from an earlier Embedding:Model; the
                // same re-embedding the API does at startup.
                var reembedded = await scope.ServiceProvider.GetRequiredService<ChunkReembedder>()
                    .ReembedAsync(existing.Id, cancellationToken);
                if (reembedded > 0)
                {
                    Console.WriteLine($"  ~ {entry.Id}: {reembedded} chunks re-embedded");
                }

                ready[entry.Id] = existing.Id;
                continue;
            }

            if (existing is not null)
            {
                // Changed title or validity, or an earlier failed run: process it afresh.
                db.Documents.Remove(existing);
                await db.SaveChangesAsync(cancellationToken);
            }

            var id = await ProcessAsync(scope.ServiceProvider, db, entry, path, content, hash, uploaderId, cancellationToken);
            if (id is not null)
            {
                ready[entry.Id] = id.Value;
            }
        }

        await RemoveUnlistedAsync(services, keptHashes, cancellationToken);
        return ready;
    }

    private static async Task<Guid?> ProcessAsync(
        IServiceProvider scope,
        AppDbContext db,
        CorpusEntry entry,
        string path,
        byte[] content,
        string hash,
        Guid uploaderId,
        CancellationToken cancellationToken)
    {
        var (contentType, error) = DocumentFileInspector.Inspect(content, path);
        if (error is not null)
        {
            Console.WriteLine($"  ! {entry.Id}: {error}");
            return null;
        }

        if (!Enum.TryParse<DocumentKind>(entry.Kind, ignoreCase: true, out var kind))
        {
            Console.WriteLine($"  ! {entry.Id}: unknown kind '{entry.Kind}'");
            return null;
        }

        var document = new Document
        {
            Id = Guid.CreateVersion7(),
            Title = entry.Title,
            Unit = entry.Unit,
            Kind = kind,
            ValidityPeriod = entry.ValidityPeriod,
            FileName = Path.GetFileName(path),
            ContentType = contentType!,
            SizeBytes = content.Length,
            ContentHash = hash,
            Status = DocumentStatus.Processing,
            AttemptCount = 1,
            UploadedByUserId = uploaderId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            File = new DocumentFile { Content = content },
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await scope.GetRequiredService<IDocumentProcessor>().ProcessAsync(document, cancellationToken);
            var chunks = await db.Chunks.CountAsync(c => c.DocumentId == document.Id, cancellationToken);
            Console.WriteLine($"  + {entry.Id}: {chunks} chunks");
            return document.Id;
        }
        catch (DocumentParseException ex)
        {
            Console.WriteLine($"  ! {entry.Id}: {ex.Message}");
            db.ChangeTracker.Clear();
            await db.Documents.Where(d => d.Id == document.Id).ExecuteDeleteAsync(cancellationToken);
            return null;
        }
    }

    private static async Task RemoveUnlistedAsync(IServiceProvider services, HashSet<string> keptHashes, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var removed = await db.Documents
            .Where(d => !keptHashes.Contains(d.ContentHash))
            .ExecuteDeleteAsync(cancellationToken);

        if (removed > 0)
        {
            Console.WriteLine($"  - removed {removed} document(s) no longer in the manifest");
        }
    }
}
