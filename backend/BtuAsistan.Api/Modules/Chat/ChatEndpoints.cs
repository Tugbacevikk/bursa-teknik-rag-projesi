using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.RateLimiting;
using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Search;
using BtuAsistan.Api.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BtuAsistan.Api.Modules.Chat;

// The students' chat (PLANNING C1–C3): anonymous, limited per IP address and overall.
public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat", AskAsync)
            .AllowAnonymous()
            .RequireRateLimiting(ApiRateLimiting.ChatPolicy);

        app.MapGet("/api/chat/{conversationId:guid}", GetConversationAsync)
            .AllowAnonymous();

        app.MapPut("/api/chat/{conversationId:guid}/messages/{messageId:guid}/feedback", SetFeedbackAsync)
            .AllowAnonymous()
            .RequireRateLimiting(ApiRateLimiting.FeedbackPolicy);

        return app;
    }

    // 👍/👎 on an answer (PLANNING D2, D3). PUT: giving it again replaces the earlier one.
    // Only an answer of this chat: knowing the chat's id is what allows it, as for reading.
    private static async Task<Results<NoContent, NotFound, ValidationProblem>> SetFeedbackAsync(
        Guid conversationId,
        Guid messageId,
        MessageFeedbackRequest request,
        AppDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request.Validate(out var feedback) is { Count: > 0 } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var message = await db.Messages.SingleOrDefaultAsync(
            m => m.Id == messageId && m.ConversationId == conversationId && m.Role == MessageRole.Assistant,
            cancellationToken);

        if (message is null)
        {
            return TypedResults.NotFound();
        }

        // A comment only explains a 👎; a later 👍 clears it.
        var comment = request.Comment?.Trim();
        message.Feedback = feedback;
        message.FeedbackComment = feedback == MessageFeedback.Down && !string.IsNullOrEmpty(comment)
            ? PersonalDataMasker.Mask(comment)
            : null;
        message.FeedbackAt = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // Server-sent events, one per ChatEvent (see ChatEvents). Problems found before the
    // answer starts are ordinary status codes; after that the status is 200 for good, so
    // they arrive as an "error" event.
    private static async Task<IResult> AskAsync(
        ChatRequest request,
        ChatService chat,
        ChatConcurrency concurrency,
        IOptions<ChatOptions> options,
        HttpContext httpContext,
        ILogger<ChatService> logger,
        CancellationToken cancellationToken)
    {
        if (request.Validate() is { Count: > 0 } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Waits in line when MaxConcurrentAnswers are being written.
        var lease = await concurrency.Limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
        {
            lease.Dispose();
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: options.Value.BusyMessage);
        }

        // Released when the response is done, however it ends.
        httpContext.Response.RegisterForDispose(lease);

        var events = chat.AskAsync(request.Question!.Trim(), request.ConversationId, cancellationToken);
        return TypedResults.ServerSentEvents(ToServerSentEvents(events, options.Value.UnavailableMessage, logger, cancellationToken));
    }

    private static async IAsyncEnumerable<SseItem<object>> ToServerSentEvents(
        IAsyncEnumerable<ChatEvent> events,
        string unavailableMessage,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var enumerator = events.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            // An unexpected failure (e.g. the database) would otherwise just cut the stream;
            // the student gets the same message as when the model is unavailable.
            ChatEvent? next;
            try
            {
                next = await enumerator.MoveNextAsync() ? enumerator.Current : null;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "The chat failed while answering.");
                next = new AnswerFailed(unavailableMessage);
            }

            if (next is null)
            {
                yield break;
            }

            yield return next switch
            {
                ConversationStarted e => new SseItem<object>(new { e.ConversationId }, "conversation"),
                SourcesFound e => new SseItem<object>(new { Sources = e.Sources.Select(ChatSource.From).ToList() }, "sources"),
                AnswerTextAdded e => new SseItem<object>(new { e.Text }, "delta"),
                AnswerCompleted e => new SseItem<object>(new { e.MessageId }, "done"),
                AnswerNotFoundEvent e => new SseItem<object>(new { e.MessageId, e.Message }, "not_found"),
                AnswerFailed e => new SseItem<object>(new { e.Message }, "error"),
                _ => throw new InvalidOperationException($"Unknown chat event {next.GetType().Name}."),
            };

            if (next is AnswerFailed)
            {
                yield break;
            }
        }
    }

    // For a page reload: the chat as the browser showed it (C2). Anyone with the id can read
    // it, like they can continue it (C3).
    private static async Task<Results<Ok<ConversationResponse>, NotFound>> GetConversationAsync(
        Guid conversationId, AppDbContext db, CancellationToken cancellationToken)
    {
        var messages = await db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new ConversationResponse(
            conversationId,
            messages.Select(m => new ChatMessage(
                m.Id,
                m.Role.ToString(),
                m.Content,
                m.Outcome?.ToString(),
                m.Sources.Select(ChatSource.From).ToList(),
                m.CreatedAt,
                m.Feedback?.ToString(),
                m.FeedbackComment)).ToList()));
    }
}

public sealed record ChatRequest(string? Question, Guid? ConversationId)
{
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Question) || Question.Trim().Length > SearchOptions.MaxQuestionLength)
        {
            errors["question"] = [$"Soru boş olamaz ve en fazla {SearchOptions.MaxQuestionLength} karakter olabilir."];
        }

        return errors;
    }
}

// Rating is "Up" or "Down" (any case); Comment is kept with Down only.
public sealed record MessageFeedbackRequest(string? Rating, string? Comment)
{
    public const int MaxCommentLength = 500;

    public Dictionary<string, string[]> Validate(out MessageFeedback feedback)
    {
        var errors = new Dictionary<string, string[]>();

        // Enum.TryParse would also take "0" or "1".
        feedback = default;
        if (Rating is null || !Rating.All(char.IsAsciiLetter) || !Enum.TryParse(Rating, ignoreCase: true, out feedback))
        {
            errors["rating"] = ["Geri bildirim \"Up\" ya da \"Down\" olmalıdır."];
        }

        if (Comment is not null && Comment.Trim().Length > MaxCommentLength)
        {
            errors["comment"] = [$"Yorum en fazla {MaxCommentLength} karakter olabilir."];
        }

        return errors;
    }
}

// What a student sees of a source: no internal ids.
public sealed record ChatSource(
    int Number,
    string Title,
    string Unit,
    string? SectionPath,
    int? PageNumber,
    string? SourceUrl,
    string? ValidityPeriod)
{
    public static ChatSource From(MessageSource source) => new(
        source.Number, source.Title, source.Unit, source.SectionPath, source.PageNumber, source.SourceUrl, source.ValidityPeriod);
}

// Role, Outcome and Feedback as their names ("User", "Answered", "Down"), like document
// status.
public sealed record ChatMessage(
    Guid Id,
    string Role,
    string Content,
    string? Outcome,
    IReadOnlyList<ChatSource> Sources,
    DateTimeOffset CreatedAt,
    string? Feedback,
    string? FeedbackComment);

public sealed record ConversationResponse(Guid ConversationId, IReadOnlyList<ChatMessage> Messages);

// The app-wide limit on answers written at once (ChatOptions.MaxConcurrentAnswers).
public sealed class ChatConcurrency(ConcurrencyLimiter limiter)
{
    public ConcurrencyLimiter Limiter { get; } = limiter;
}
