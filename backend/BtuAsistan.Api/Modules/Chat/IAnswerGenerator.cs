namespace BtuAsistan.Api.Modules.Chat;

// Writes an answer from the question and its sources. Implemented over HTTP by
// AiServiceAnswerGenerator (the prompts live in the AI service, PLANNING C5); tests can
// substitute their own.
public interface IAnswerGenerator
{
    /// <summary>
    /// The answer as it is written: any number of <see cref="AnswerDelta"/>, then exactly one
    /// <see cref="AnswerFinished"/> or <see cref="AnswerNotFound"/>. Throws
    /// <see cref="AnswerGenerationException"/> when the answer cannot be written, also
    /// after some text has arrived.
    /// </summary>
    IAsyncEnumerable<GenerationEvent> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken);
}

public sealed record GenerationRequest(
    string Question,
    IReadOnlyList<GenerationTurn> History,
    IReadOnlyList<GenerationSource> Sources);

// Role is "user" or "assistant".
public sealed record GenerationTurn(string Role, string Text);

// N is the number the answer cites as [N].
public sealed record GenerationSource(int N, string Title, string? Section, string Text);

public abstract record GenerationEvent;

public sealed record AnswerDelta(string Text) : GenerationEvent;

public sealed record AnswerFinished(string Model) : GenerationEvent;

// The sources do not answer the question (C7). Model: the chat model that decided so.
public sealed record AnswerNotFound(string Model) : GenerationEvent;

// The AI service or the chat model could not help. Not the student's fault: they see a
// "try again later" message, and nothing is stored.
public sealed class AnswerGenerationException : Exception
{
    public AnswerGenerationException(string message)
        : base(message)
    {
    }

    public AnswerGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
