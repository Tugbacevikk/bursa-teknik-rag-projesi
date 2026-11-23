using BtuAsistan.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BtuAsistan.Api.Modules.Documents;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public const int TitleMaxLength = 300;
    public const int UnitMaxLength = 100;
    public const int SourceUrlMaxLength = 2048;
    public const int ValidityPeriodMaxLength = 50;
    public const int FileNameMaxLength = 255;
    public const int FailureReasonMaxLength = 1000;

    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Title).HasMaxLength(TitleMaxLength);
        builder.Property(d => d.Unit).HasMaxLength(UnitMaxLength);
        builder.Property(d => d.SourceUrl).HasMaxLength(SourceUrlMaxLength);
        builder.Property(d => d.ValidityPeriod).HasMaxLength(ValidityPeriodMaxLength);
        builder.Property(d => d.FileName).HasMaxLength(FileNameMaxLength);
        builder.Property(d => d.ContentType).HasMaxLength(100);
        builder.Property(d => d.FailureReason).HasMaxLength(FailureReasonMaxLength);

        // Enums as text: readable in the table, and reordering an enum is harmless.
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);

        // 64 hex characters of SHA-256. The database itself rejects a second copy.
        builder.Property(d => d.ContentHash).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(d => d.ContentHash).IsUnique();

        builder.Property(d => d.Version).HasDefaultValue(1);
        builder.Property(d => d.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(d => d.UpdatedAt).HasDefaultValueSql("now()");

        // Staff accounts are deactivated, not deleted; an account that uploaded documents
        // cannot be removed by accident.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.File)
            .WithOne()
            .HasForeignKey<DocumentFile>(f => f.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.Chunks)
            .WithOne()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DocumentFileConfiguration : IEntityTypeConfiguration<DocumentFile>
{
    public void Configure(EntityTypeBuilder<DocumentFile> builder)
    {
        builder.ToTable("document_files");

        builder.HasKey(f => f.DocumentId);

        builder.Property(f => f.Content).HasColumnType("bytea");
    }
}
