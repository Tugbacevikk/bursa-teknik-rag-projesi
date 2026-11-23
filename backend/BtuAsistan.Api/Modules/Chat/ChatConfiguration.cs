using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BtuAsistan.Api.Modules.Chat;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(c => c.LastMessageAt).HasDefaultValueSql("now()");

        // For the retention clean-up (Faz 5): "chats idle for more than N days".
        builder.HasIndex(c => c.LastMessageAt);

        builder.HasMany(c => c.Messages)
            .WithOne()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");

        builder.HasKey(m => m.Id);

        // Enums as text, as for documents.
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Feedback).HasConversion<string>().HasMaxLength(10);
        // Room above the 500 characters allowed: a masked id number grows by three.
        builder.Property(m => m.FeedbackComment).HasMaxLength(1000);
        builder.Property(m => m.Model).HasMaxLength(100);
        builder.Property(m => m.CreatedAt).HasDefaultValueSql("now()");

        // One jsonb column rather than a table: the list is written once with the answer,
        // always read with it, and never searched on its own.
        builder.OwnsMany(m => m.Sources, sources => sources.ToJson());

        // A chat's messages in order.
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
    }
}
