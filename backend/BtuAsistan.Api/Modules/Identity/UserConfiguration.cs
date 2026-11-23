using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BtuAsistan.Api.Modules.Identity;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    // RFC 5321 limits an email address to 254 characters.
    public const int EmailMaxLength = 254;

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email)
            .HasMaxLength(EmailMaxLength);

        builder.Property(u => u.NormalizedEmail)
            .HasMaxLength(EmailMaxLength);

        // The database itself rejects a second account with the same email.
        builder.HasIndex(u => u.NormalizedEmail)
            .IsUnique();

        // Stored as text so the table stays readable and reordering the enum is harmless.
        builder.Property(u => u.Role)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(u => u.IsActive)
            .HasDefaultValue(true);

        // Accounts made with create-admin chose their own password.
        builder.Property(u => u.MustChangePassword)
            .HasDefaultValue(false);

        builder.Property(u => u.CreatedAt)
            .HasDefaultValueSql("now()");
    }
}
