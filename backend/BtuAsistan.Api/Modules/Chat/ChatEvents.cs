namespace BtuAsistan.Api.Modules.Chat;

// What ChatService reports while answering, in this order (PLANNING C2):
// ConversationStarted, then SourcesFound (when there are sources), then AnswerTextAdded*,
// then exactly one of AnswerCompleted, AnswerNotFoundEvent or AnswerFailed. The endpoint
// turns each into one server-sent event.
public abstract record ChatEvent;

// The chat this answer belongs to. A new id when none was given or the given one no longer
// exists (e.g. deleted after the retention period); the browser keeps it for the next question.
public sealed record ConversationStarted(Guid ConversationId) : ChatEvent;

// Before the answer, so the [n] in it can be linked as soon as it appears.
public sealed record SourcesFound(IReadOnlyList<MessageSource> Sources) : ChatEvent;

public sealed record AnswerTextAdded(string Text) : ChatEvent;

public sealed record AnswerCompleted(Guid MessageId) : ChatEvent;

// The documents do not answer the question. The sources sent earlier, if any, are not
// the answer's and should not be shown.
public sealed record AnswerNotFoundEvent(Guid MessageId, string Message) : ChatEvent;

// Nothing was stored; the text sent so far should be dropped.
public sealed record AnswerFailed(string Message) : ChatEvent;
