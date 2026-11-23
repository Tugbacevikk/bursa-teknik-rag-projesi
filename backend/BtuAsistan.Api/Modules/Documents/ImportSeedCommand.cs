using System.Security.Cryptography;
using System.Text.Json;
using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Audit;
using BtuAsistan.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Api.Modules.Documents;

/// <summary>
/// dotnet run --project backend/BtuAsistan.Api -- import-seed --as &lt;staff email&gt; [--dir data/seed]
///
/// Stores the seed documents listed in sources.json as Pending; the running API processes
/// them like uploads. Only entries with a sourceUrl are imported (PLANNING P14): information
/// whose official source nobody has checked is not given to students. Running it again is
/// safe: a file already imported is recognized by its content hash and skipped.
/// </summary>
public static class ImportSeedCommand
{
    public const string Name = "import-seed";

    private const string Usage = $"Usage: dotnet run --project backend/BtuAsistan.Api -- {Name} --as <staff email> [--dir data/seed]";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var options = ParseArguments(args);
        if (options is null)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var manifestPath = Path.Combine(options.Directory, "sources.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"Not found: {Path.GetFullPath(manifestPath)}");
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        // Documents need an uploader; the audit log then shows who ran the import.
        var normalizedEmail = User.NormalizeEmail(options.UploaderEmail);
        var uploader = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail && u.IsActive);
        if (uploader is null)
        {
            Console.Error.WriteLine($"No active staff account with the email {options.UploaderEmail}.");
            return 1;
        }

        SeedManifest manifest;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<SeedManifest>(stream, JsonSerializerOptions.Web)
                ?? throw new JsonException("The file is empty.");
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"sources.json could not be read: {ex.Message}");
            return 1;
        }

        var counts = new Dictionary<string, int>();
        foreach (var entry in manifest.Documents)
        {
            var (outcome, message) = await ImportAsync(db, timeProvider, uploader, options.Directory, entry);
            counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
            Console.WriteLine($"{outcome,-16} {entry.File}{(message is null ? "" : $": {message}")}");
        }

        Console.WriteLine();
        Console.WriteLine(string.Join(", ", counts.Select(c => $"{c.Key}: {c.Value}")));
        if (counts.GetValueOrDefault(Outcomes.Queued) > 0)
        {
            Console.WriteLine("The running API processes queued documents within 5 minutes, or at its next start.");
        }

        var problems = counts.GetValueOrDefault(Outcomes.Invalid) + counts.GetValueOrDefault(Outcomes.Rejected);
        return problems == 0 ? 0 : 1;
    }

    private static async Task<(string Outcome, string? Message)> ImportAsync(
        AppDbContext db, TimeProvider timeProvider, User uploader, string directory, SeedEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.File) || entry.File != Path.GetFileName(entry.File))
        {
            return (Outcomes.Invalid, "\"file\" must be a file name inside the seed directory.");
        }

        if (string.IsNullOrWhiteSpace(entry.SourceUrl))
        {
            return (Outcomes.NoSource, entry.SourceHint is null ? null : $"hint: {entry.SourceHint}");
        }

        var errors = DocumentMetadata.Validate(
            entry.Title, entry.Unit, entry.Kind, entry.SourceUrl, entry.ValidityPeriod, out var kind);
        if (errors.Count > 0)
        {
            return (Outcomes.Invalid, string.Join(" ", errors.Values.SelectMany(e => e)));
        }

        var path = Path.Combine(directory, entry.File);
        if (!File.Exists(path))
        {
            return (Outcomes.Invalid, "file not found");
        }

        var content = await File.ReadAllBytesAsync(path);
        var (contentType, error) = DocumentFileInspector.Inspect(content, entry.File);
        if (error is not null)
        {
            return (Outcomes.Rejected, error);
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(content));
        var existing = await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.ContentHash == hash);
        if (existing is not null)
        {
            return (Outcomes.AlreadyImported, existing.Title);
        }

        var now = timeProvider.GetUtcNow();
        var document = new Document
        {
            Id = Guid.CreateVersion7(),
            Title = entry.Title!.Trim(),
            Unit = entry.Unit!.Trim(),
            Kind = kind,
            SourceUrl = entry.SourceUrl.Trim(),
            ValidityPeriod = string.IsNullOrWhiteSpace(entry.ValidityPeriod) ? null : entry.ValidityPeriod.Trim(),
            FileName = entry.File,
            ContentType = contentType!,
            SizeBytes = content.Length,
            ContentHash = hash,
            UploadedByUserId = uploader.Id,
            CreatedAt = now,
            UpdatedAt = now,
            File = new DocumentFile { Content = content },
        };

        db.Documents.Add(document);
        db.AddAuditEntry(uploader.Id, AuditActions.DocumentUploaded, AuditEntityTypes.Document, document.Id,
            new { document.Title, document.FileName, document.Version, Source = Name });

        // One document per save: a problem with one entry does not undo the others.
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (Outcomes.Queued, null);
    }

    // `dotnet run --project` runs the app from the project folder, not from where the command
    // was typed, so a plain relative "data/seed" would miss. Without --dir, the folders above
    // are searched for the repository's data/seed.
    private static string FindSeedDirectory()
    {
        for (var folder = new DirectoryInfo(Directory.GetCurrentDirectory()); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "data", "seed");
            if (File.Exists(Path.Combine(candidate, "sources.json")))
            {
                return candidate;
            }
        }

        return Path.Combine("data", "seed");
    }

    private static CommandOptions? ParseArguments(string[] args)
    {
        string? email = null;
        string? directory = null;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--as" when i + 1 < args.Length:
                    email = args[++i];
                    break;
                case "--dir" when i + 1 < args.Length:
                    directory = args[++i];
                    break;
                default:
                    return null;
            }
        }

        return email is null || !email.Contains('@')
            ? null
            : new CommandOptions(email.Trim(), directory ?? FindSeedDirectory());
    }

    private static class Outcomes
    {
        public const string Queued = "queued";
        public const string AlreadyImported = "already-imported";
        public const string NoSource = "no-source";
        public const string Invalid = "invalid";
        public const string Rejected = "rejected";
    }

    private sealed record CommandOptions(string UploaderEmail, string Directory);

    private sealed record SeedManifest(List<SeedEntry> Documents);

    private sealed record SeedEntry(
        string? File,
        string? Title,
        string? Unit,
        string? Kind,
        string? SourceUrl,
        string? ValidityPeriod,
        string? SourceHint);
}
