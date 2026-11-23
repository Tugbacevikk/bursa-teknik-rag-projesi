namespace BtuAsistan.Api.Modules.Analytics;

// Both days included, in the school's time zone.
public sealed record AnalyticsPeriod(DateOnly From, DateOnly To);

public sealed record AnalyticsSummaryResponse(
    AnalyticsPeriod Period,
    // Questions stored, i.e. answered or NotFound: a question whose answer failed is not
    // stored (PLANNING C9).
    int Questions,
    int Answered,
    int NotFound,
    int Up,
    int Down,
    // Every day of the period, quiet days included.
    IReadOnlyList<AnalyticsDay> Days);

public sealed record AnalyticsDay(DateOnly Date, int Questions, int Answered, int NotFound, int Up, int Down);

public sealed record AnalyticsListResponse<T>(AnalyticsPeriod Period, int Total, IReadOnlyList<T> Items);

// An answer with the question it replied to: for the unanswered and 👎 lists.
public sealed record AnsweredQuestion(
    Guid ConversationId,
    Guid MessageId,
    string Question,
    // For NotFound, Chat:NotFoundMessage as the student saw it.
    string Answer,
    string Outcome,
    string? Feedback,
    string? FeedbackComment,
    DateTimeOffset AnsweredAt);

public sealed record TopDocument(
    Guid DocumentId,
    // Current title, or the one stored with the answers when the document was deleted.
    string Title,
    string Unit,
    bool Deleted,
    // Answers citing it at least once.
    int Answers);

public sealed record TopQuestion(
    // The latest wording among those grouped.
    string Question,
    int Count,
    DateTimeOffset LastAskedAt);

// A chat as staff see it: with ids, the model and timings.
public sealed record StaffConversationResponse(Guid ConversationId, DateTimeOffset CreatedAt, IReadOnlyList<StaffMessage> Messages);

public sealed record StaffMessage(
    Guid Id,
    string Role,
    string Content,
    string? Outcome,
    IReadOnlyList<StaffSource> Sources,
    string? Model,
    int? DurationMs,
    string? Feedback,
    string? FeedbackComment,
    DateTimeOffset CreatedAt);

public sealed record StaffSource(int Number, Guid DocumentId, string Title, string Unit, string? SectionPath, int? PageNumber);
