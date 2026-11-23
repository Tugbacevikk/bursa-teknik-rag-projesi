using BtuAsistan.Api.Modules.Audit;
using BtuAsistan.Api.Modules.Chat;
using BtuAsistan.Api.Modules.Documents;
using BtuAsistan.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;

namespace BtuAsistan.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentFile> DocumentFiles => Set<DocumentFile>();

    public DbSet<Chunk> Chunks => Set<Chunk>();

    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
