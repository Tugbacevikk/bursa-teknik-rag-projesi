namespace BtuAsistan.Api.Modules.Chat;

public enum MessageRole
{
    User,
    Assistant,
}

// How an assistant message ended. Only finished answers are stored (C9): an answer cut off
// by an error or a closed browser leaves the question without a reply.
public enum MessageOutcome
{
    // Written from the sources, with citations.
    Answered,

    // The sources did not hold the answer; the student saw Chat:NotFoundMessage. Counted
    // as "unanswered" in Faz 5's analytics.
    NotFound,
}

// A student's 👍/👎 on an answer (PLANNING D2).
public enum MessageFeedback
{
    Up,
    Down,
}

public class Message
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public MessageRole Role { get; set; }

    // The question with personal data masked (C10), or the answer as the student saw it.
    public required string Content { get; set; }

    // Assistant messages only.
    public MessageOutcome? Outcome { get; set; }

    // The sources the answer was written from, numbered as in the answer's [n]. A copy, not
    // a reference (C8): when a document is replaced or deleted, past answers keep their
    // citations. Empty for questions and for NotFound without sources.
    public List<MessageSource> Sources { get; set; } = [];

    // The chat model that wrote the answer or found the sources lacking, e.g.
    // "gemini-3.5-flash-lite"; null when no model was asked (nothing found by the search).
    public string? Model { get; set; }

    // Question received to answer finished, for assistant messages.
    public int? DurationMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Assistant messages only, and only once the student has given it; the last one given
    // counts (D2).
    public MessageFeedback? Feedback { get; set; }

    // With Down only, personal data masked like the question.
    public string? FeedbackComment { get; set; }

    public DateTimeOffset? FeedbackAt { get; set; }
}

// One numbered source of an answer, as shown under it.
public class MessageSource
{
    public int Number { get; set; }

    public Guid DocumentId { get; set; }

    public Guid ChunkId { get; set; }

    public required string Title { get; set; }

    public required string Unit { get; set; }

    public string? SectionPath { get; set; }

    public int? PageNumber { get; set; }

    public string? SourceUrl { get; set; }

    public string? ValidityPeriod { get; set; }
}
