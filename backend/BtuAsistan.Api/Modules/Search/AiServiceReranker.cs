using System.Net.Http.Json;
using System.Text.Json;

namespace BtuAsistan.Api.Modules.Search;

/// <summary>
/// Calls the Python AI service. The contract it implements:
///
///   POST /rerank
///   request:  { "query": "...", "texts": ["...", "..."] }
///   response: { "model": "BAAI/bge-reranker-v2-m3", "scores": [0.71, 0.02, ...] }
///   503 when the reranker is disabled in the service, still loading or failed to load.
/// </summary>
public sealed class AiServiceReranker(HttpClient httpClient) : IReranker
{
    public async Task<IReadOnlyList<double>> ScoreAsync(
        string question, IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        RerankResponse? response;
        try
        {
            using var message = await httpClient.PostAsJsonAsync("/rerank", new RerankRequest(question, texts), cancellationToken);
            if (!message.IsSuccessStatusCode)
            {
                throw new RerankerUnavailableException($"The AI service answered {(int)message.StatusCode}.");
            }

            response = await message.Content.ReadFromJsonAsync<RerankResponse>(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new RerankerUnavailableException("The AI service cannot be reached.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation.
            throw new RerankerUnavailableException("The reranker did not answer in time.", ex);
        }
        catch (JsonException ex)
        {
            throw new RerankerUnavailableException("The AI service sent an answer that is not valid JSON.", ex);
        }

        if (response?.Scores is null || response.Scores.Count != texts.Count)
        {
            throw new RerankerUnavailableException(
                $"Expected {texts.Count} scores, got {response?.Scores?.Count ?? 0}.");
        }

        return response.Scores;
    }

    private sealed record RerankRequest(string Query, IReadOnlyList<string> Texts);

    private sealed record RerankResponse(string? Model, List<double>? Scores);
}
