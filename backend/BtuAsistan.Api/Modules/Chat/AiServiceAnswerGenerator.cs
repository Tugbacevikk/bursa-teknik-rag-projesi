using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace BtuAsistan.Api.Modules.Chat;

/// <summary>
/// Calls the Python AI service. The contract it implements (PLANNING C5):
///
///   POST /generate
///   request:  { "question": "...", "history": [{ "role": "user", "text": "..." }],
///               "sources": [{ "n": 1, "title": "...", "section": "...", "text": "..." }] }
///   response: newline-delimited JSON, one event per line, as the model writes:
///             { "type": "delta", "text": "..." }   any number of times, then one of
///             { "type": "done", "model": "gemini-..." }
///             { "type": "not_found", "model": "gemini-..." }
///             { "type": "error", "message": "..." }   also after some deltas
///   503 when the service has no chat model configured.
/// </summary>
public sealed class AiServiceAnswerGenerator(HttpClient httpClient) : IAnswerGenerator
{
    public async IAsyncEnumerable<GenerationEvent> GenerateAsync(
        GenerationRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, cancellationToken);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(body);

        while (await ReadLineAsync(reader, cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var generationEvent = Parse(line);
            yield return generationEvent;

            if (generationEvent is not AnswerDelta)
            {
                yield break;
            }
        }

        // The connection closed in the middle of the answer.
        throw new AnswerGenerationException("The AI service stopped before the answer was finished.");
    }

    private async Task<HttpResponseMessage> SendAsync(GenerationRequest request, CancellationToken cancellationToken)
    {
        var body = new GenerateRequest(
            request.Question,
            request.History.Select(t => new GenerateTurn(t.Role, t.Text)).ToList(),
            request.Sources.Select(s => new GenerateSource(s.N, s.Title, s.Section, s.Text)).ToList());

        try
        {
            // ResponseHeadersRead: return as soon as the answer starts, not when it ends, so
            // its lines can be passed on while the model is still writing.
            using var message = new HttpRequestMessage(HttpMethod.Post, "/generate") { Content = JsonContent.Create(body) };
            var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new AnswerGenerationException($"The AI service answered {status}.");
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            throw new AnswerGenerationException("The AI service cannot be reached.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation.
            throw new AnswerGenerationException("The AI service did not start answering in time.", ex);
        }
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadLineAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            throw new AnswerGenerationException("The connection to the AI service broke.", ex);
        }
    }

    private static GenerationEvent Parse(string line)
    {
        EventLine? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<EventLine>(line, JsonSerializerOptions.Web);
        }
        catch (JsonException ex)
        {
            throw new AnswerGenerationException("The AI service sent a line that is not valid JSON.", ex);
        }

        return parsed?.Type switch
        {
            "delta" when parsed.Text is not null => new AnswerDelta(parsed.Text),
            "done" => new AnswerFinished(parsed.Model ?? "unknown"),
            "not_found" => new AnswerNotFound(parsed.Model ?? "unknown"),
            "error" => throw new AnswerGenerationException($"The chat model failed: {parsed.Message}"),
            _ => throw new AnswerGenerationException($"The AI service sent an unknown event '{parsed?.Type}'."),
        };
    }

    private sealed record GenerateRequest(string Question, List<GenerateTurn> History, List<GenerateSource> Sources);

    private sealed record GenerateTurn(string Role, string Text);

    private sealed record GenerateSource(int N, string Title, string? Section, string Text);

    private sealed record EventLine(string? Type, string? Text, string? Model, string? Message);
}
