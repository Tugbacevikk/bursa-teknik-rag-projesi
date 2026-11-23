using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using BtuAsistan.Api.Modules.Chat;
using BtuAsistan.Api.Modules.Documents;
using BtuAsistan.Api.Modules.Embeddings;
using Microsoft.Extensions.Options;
using Pgvector;

namespace BtuAsistan.Api.Tests.Infrastructure;

/// <summary>
/// Stands in for bge-m3 (PLANNING Testler, T3): a bag of words, each word adding to one of
/// the 1024 dimensions chosen by its hash, then normalized. Texts sharing words end up
/// close, so vector search finds them; the same text always gives the same vector.
/// </summary>
public sealed class FakeEmbeddingClient(IOptions<EmbeddingOptions> options) : IEmbeddingClient
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    // Set by a test to make the "AI service" unavailable.
    public bool Unavailable { get; set; }

    public int Calls { get; private set; }

    public Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        Calls++;
        if (Unavailable)
        {
            throw new EmbeddingUnavailableException("The fake AI service is down.");
        }

        return Task.FromResult(new EmbeddingResult(options.Value.Model, texts.Select(Embed).ToList()));
    }

    public static Vector Embed(string text)
    {
        var values = new float[ChunkConfiguration.EmbeddingDimensions];
        foreach (var word in Words(text))
        {
            values[(int)((uint)StableHash(word) % (uint)values.Length)] += 1;
        }

        var length = MathF.Sqrt(values.Sum(v => v * v));
        if (length == 0)
        {
            // An empty text still needs a valid vector for cosine distance.
            values[0] = 1;
            length = 1;
        }

        return new Vector(values.Select(v => v / length).ToArray());
    }

    private static IEnumerable<string> Words(string text)
    {
        var word = new StringBuilder();
        foreach (var c in text.ToLower(Turkish))
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(c);
            }
            else if (word.Length > 0)
            {
                yield return word.ToString();
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            yield return word.ToString();
        }
    }

    // string.GetHashCode differs between runs; this does not.
    private static int StableHash(string word)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in word)
            {
                hash = (hash ^ c) * 16777619;
            }

            return hash;
        }
    }
}

/// <summary>
/// Stands in for the AI service's /generate. By default answers "Cevap [1]." citing the
/// first source; a test sets <see cref="Respond"/> for anything else, and reads what the
/// API asked in <see cref="Requests"/>.
/// </summary>
public sealed class FakeAnswerGenerator : IAnswerGenerator
{
    public const string ModelName = "fake-chat-model";

    public Func<GenerationRequest, IAsyncEnumerable<GenerationEvent>> Respond { get; set; } = _ => Answer("Cevap ", "[1].");

    public List<GenerationRequest> Requests { get; } = [];

    public IAsyncEnumerable<GenerationEvent> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        return Respond(request);
    }

    // The answer in the given parts, then finished.
    public static async IAsyncEnumerable<GenerationEvent> Answer(params string[] parts)
    {
        foreach (var part in parts)
        {
            await Task.Yield();
            yield return new AnswerDelta(part);
        }

        yield return new AnswerFinished(ModelName);
    }

    public static async IAsyncEnumerable<GenerationEvent> NotFound()
    {
        await Task.Yield();
        yield return new AnswerNotFound(ModelName);
    }

    // Some text, then the model fails, as when Gemini breaks off mid-answer.
    public static async IAsyncEnumerable<GenerationEvent> FailAfter(string part)
    {
        await Task.Yield();
        yield return new AnswerDelta(part);
        throw new AnswerGenerationException("The fake chat model failed.");
    }

    // Holds the answer until the test releases it: for "how many answers at once" tests.
    public static async IAsyncEnumerable<GenerationEvent> WaitFor(Task release, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await release.WaitAsync(cancellationToken);
        yield return new AnswerDelta("Cevap [1].");
        yield return new AnswerFinished(ModelName);
    }
}
