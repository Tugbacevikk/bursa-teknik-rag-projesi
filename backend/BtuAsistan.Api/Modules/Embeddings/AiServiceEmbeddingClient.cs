using System.Net.Http.Json;
using System.Text.Json;
using BtuAsistan.Api.Modules.Documents;
using Microsoft.Extensions.Options;
using Pgvector;

namespace BtuAsistan.Api.Modules.Embeddings;

/// <summary>
/// Calls the Python AI service. The contract the service implements:
///
///   POST /embed
///   request:  { "texts": ["...", "..."] }
///   response: { "model": "BAAI/bge-m3", "dimensions": 1024, "embeddings": [[0.01, ...], ...] }
///   503 while the model is still loading.
///
/// Vectors are unit length (normalized), so cosine similarity does not depend on text length.
/// </summary>
public sealed class AiServiceEmbeddingClient(
    HttpClient httpClient,
    IOptions<EmbeddingOptions> options,
    ILogger<AiServiceEmbeddingClient> logger) : IEmbeddingClient
{
    public async Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var vectors = new List<Vector>(texts.Count);

        foreach (var batch in texts.Chunk(settings.BatchSize))
        {
            var response = await PostAsync(batch, cancellationToken);
            Check(response, batch.Length, settings.Model);
            vectors.AddRange(response.Embeddings!.Select(values => new Vector(values)));
        }

        return new EmbeddingResult(settings.Model, vectors);
    }

    private async Task<EmbedResponse> PostAsync(string[] batch, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync("/embed", new EmbedRequest(batch), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new EmbeddingUnavailableException($"The AI service answered {(int)response.StatusCode}.");
            }

            return await response.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken)
                ?? throw new EmbeddingUnavailableException("The AI service sent an empty answer.");
        }
        catch (HttpRequestException ex)
        {
            throw new EmbeddingUnavailableException("The AI service cannot be reached.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation.
            throw new EmbeddingUnavailableException("The AI service did not answer in time.", ex);
        }
        catch (JsonException ex)
        {
            throw new EmbeddingUnavailableException("The AI service sent an answer that is not valid JSON.", ex);
        }
    }

    // A wrong model or size would be stored without complaint and silently break search,
    // so every answer is checked before use.
    private void Check(EmbedResponse response, int expectedCount, string expectedModel)
    {
        if (response.Model != expectedModel)
        {
            logger.LogError(
                "The AI service uses model {Actual}, but {Expected} is configured (Embedding:Model).",
                response.Model, expectedModel);
            throw new EmbeddingUnavailableException($"The AI service uses model '{response.Model}', not '{expectedModel}'.");
        }

        if (response.Embeddings is null || response.Embeddings.Count != expectedCount)
        {
            throw new EmbeddingUnavailableException(
                $"Expected {expectedCount} vectors, got {response.Embeddings?.Count ?? 0}.");
        }

        if (response.Embeddings.Any(values => values.Length != ChunkConfiguration.EmbeddingDimensions))
        {
            throw new EmbeddingUnavailableException(
                $"Expected vectors of {ChunkConfiguration.EmbeddingDimensions} numbers.");
        }
    }

    private sealed record EmbedRequest(IReadOnlyList<string> Texts);

    private sealed record EmbedResponse(string? Model, int Dimensions, List<float[]>? Embeddings);
}
