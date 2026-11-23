using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using BtuAsistan.Api.Data;
using BtuAsistan.Api.Modules.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BtuAsistan.Api.Modules.Chat;

// One question, start to finish (PLANNING Faz 4): mask personal data, search, have the
// answer written, store it. Reports progress as ChatEvents for the endpoint to stream.
public sealed partial class ChatService(
    AppDbContext db,
    IChunkRetrieval retrieval,
    IAnswerGenerator generator,
    IOptions<ChatOptions> options,
    TimeProvider timeProvider,
    ILogger<ChatService> logger)
{
    public async IAsyncEnumerable<ChatEvent> AskAsync(
        string question, Guid? conversationId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var askedAt = timeProvider.GetUtcNow();

        // Before anything else: the masked question is what is searched, sent to the model
        // and stored (C10).
        var masked = PersonalDataMasker.Mask(question.Trim());

        var conversation = conversationId is { } id
            ? await db.Conversations.FindAsync([id], cancellationToken)
            : null;
        var isNew = conversation is null;
        conversation ??= new Conversation { Id = Guid.NewGuid(), CreatedAt = askedAt, LastMessageAt = askedAt };
        yield return new ConversationStarted(conversation.Id);

        var history = isNew ? [] : await LoadHistoryAsync(conversation.Id, settings.HistoryMessageCount, cancellationToken);

        // Only the last question is searched (C4).
        var found = await retrieval.RetrieveAsync(new RetrievalRequest(masked), cancellationToken);
        if (found.Chunks.Count == 0)
        {
            // Nothing to answer from: the model is not asked.
            var messageId = await SaveAsync(conversation, isNew, masked, askedAt, MessageOutcome.NotFound, settings.NotFoundMessage, [], model: null);
            yield return new AnswerNotFoundEvent(messageId, settings.NotFoundMessage);
            yield break;
        }

        var sources = found.Chunks.Select((chunk, index) => new MessageSource
        {
            Number = index + 1,
            DocumentId = chunk.DocumentId,
            ChunkId = chunk.ChunkId,
            Title = chunk.DocumentTitle,
            Unit = chunk.Unit,
            SectionPath = chunk.SectionPath,
            PageNumber = chunk.PageNumber,
            SourceUrl = chunk.SourceUrl,
            ValidityPeriod = chunk.ValidityPeriod,
        }).ToList();
        yield return new SourcesFound(sources);

        // The model reads the chunk with its context header ("Belge: … > …"), as it was embedded.
        var request = new GenerationRequest(
            masked,
            history,
            found.Chunks.Select((chunk, index) => new GenerationSource(index + 1, chunk.DocumentTitle, chunk.SectionPath, chunk.Content)).ToList());

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(settings.GenerationTimeoutSeconds));
        await using var events = generator.GenerateAsync(request, deadline.Token).GetAsyncEnumerator(deadline.Token);

        var answer = new StringBuilder();
        while (true)
        {
            // Only the step that can fail is inside try: C# does not allow yield there.
            GenerationEvent? next;
            string? failure = null;
            try
            {
                next = await events.MoveNextAsync() ? events.Current : null;
            }
            catch (AnswerGenerationException ex)
            {
                next = null;
                failure = ex.Message;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                next = null;
                failure = $"No complete answer within {settings.GenerationTimeoutSeconds} s.";
            }

            switch (next)
            {
                case AnswerDelta delta:
                    answer.Append(delta.Text);
                    yield return new AnswerTextAdded(delta.Text);
                    break;

                case AnswerFinished finished:
                    var answerId = await SaveAsync(conversation, isNew, masked, askedAt, MessageOutcome.Answered, answer.ToString(), sources, finished.Model);
                    yield return new AnswerCompleted(answerId);
                    yield break;

                case AnswerNotFound notFound:
                    var notFoundId = await SaveAsync(conversation, isNew, masked, askedAt, MessageOutcome.NotFound, settings.NotFoundMessage, [], notFound.Model);
                    yield return new AnswerNotFoundEvent(notFoundId, settings.NotFoundMessage);
                    yield break;

                default:
                    // Nothing is stored for a broken answer (C9); the student can ask again.
                    logger.LogWarning("Could not answer in conversation {ConversationId}: {Reason}",
                        conversation.Id, failure ?? "the answer ended without a result");
                    yield return new AnswerFailed(settings.UnavailableMessage);
                    yield break;
            }
        }
    }

    // The chat's last messages, oldest first. Citations are taken out of earlier answers:
    // their [n] point at that answer's sources, not this one's.
    private async Task<IReadOnlyList<GenerationTurn>> LoadHistoryAsync(Guid conversationId, int count, CancellationToken cancellationToken)
    {
        if (count == 0)
        {
            return [];
        }

        var recent = await db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(count)
            .Select(m => new { m.Role, m.Content })
            .ToListAsync(cancellationToken);

        recent.Reverse();
        return recent
            .Select(m => m.Role == MessageRole.User
                ? new GenerationTurn("user", m.Content)
                : new GenerationTurn("assistant", Citation().Replace(m.Content, "").Trim()))
            .ToList();
    }

    // The question and its answer together, once the answer is complete. Not cancelled when
    // the browser goes away at the last moment: a finished answer is kept.
    private async Task<Guid> SaveAsync(
        Conversation conversation,
        bool isNew,
        string question,
        DateTimeOffset askedAt,
        MessageOutcome outcome,
        string answer,
        List<MessageSource> sources,
        string? model)
    {
        var answeredAt = timeProvider.GetUtcNow();

        if (isNew)
        {
            db.Conversations.Add(conversation);
        }

        conversation.LastMessageAt = answeredAt;

        // Times set here rather than by the database: now() is the same for both rows of one
        // transaction, and the order of question and answer must be clear.
        var reply = new Message
        {
            Id = Guid.CreateVersion7(answeredAt),
            ConversationId = conversation.Id,
            Role = MessageRole.Assistant,
            Content = answer,
            Outcome = outcome,
            Sources = sources,
            Model = model,
            DurationMs = (int)(answeredAt - askedAt).TotalMilliseconds,
            CreatedAt = answeredAt,
        };

        db.Messages.AddRange(
            new Message
            {
                Id = Guid.CreateVersion7(askedAt),
                ConversationId = conversation.Id,
                Role = MessageRole.User,
                Content = question,
                CreatedAt = askedAt,
            },
            reply);

        await db.SaveChangesAsync(CancellationToken.None);
        return reply.Id;
    }

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex Citation();
}
