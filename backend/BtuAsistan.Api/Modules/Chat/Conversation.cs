namespace BtuAsistan.Api.Modules.Chat;

// One anonymous chat (PLANNING C1, C3). Nothing here says who asked: no account, address
// or browser details.
public class Conversation
{
    // Also the key the browser keeps: whoever knows it can read and continue the chat, so it
    // must not be guessable. Guid.NewGuid() (version 4) is 122 random bits from the system's
    // secure random source; version 7, used elsewhere, starts with a timestamp and would
    // make neighbouring chats easier to guess.
    public Guid Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Retention counts from here (Chat:RetentionDays, C9): a chat still in use is kept.
    public DateTimeOffset LastMessageAt { get; set; }

    public List<Message> Messages { get; set; } = [];
}
