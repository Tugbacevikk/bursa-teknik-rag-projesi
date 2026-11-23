using BtuAsistan.Api.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BtuAsistan.Api.Modules.Audit;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();

        builder.Property(a => a.OccurredAt).HasDefaultValueSql("now()");
        builder.Property(a => a.Action).HasMaxLength(50);
        builder.Property(a => a.EntityType).HasMaxLength(50);
        builder.Property(a => a.Details).HasColumnType("jsonb");

        // The log outlives the account; the entry stays and loses only the link.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.OccurredAt);
    }
}
