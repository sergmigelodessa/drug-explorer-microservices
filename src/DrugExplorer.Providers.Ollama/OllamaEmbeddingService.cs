using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DrugExplorer.ProviderAbstractions.Interfaces;

namespace DrugExplorer.Providers.Ollama;

public sealed class OllamaEmbeddingService : IEmbeddingService
{
    private const string EmbeddingsEndpoint = "api/embeddings";

    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaEmbeddingService(HttpClient httpClient, string model)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _httpClient = httpClient;
        _model = model;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        using var response = await _httpClient.PostAsJsonAsync(
            EmbeddingsEndpoint,
            new OllamaEmbeddingRequest(_model, text),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(
            cancellationToken: cancellationToken);

        if (result?.Embedding is not { Length: > 0 } embedding)
        {
            throw new InvalidDataException("Ollama returned an empty embedding.");
        }

        return embedding;
    }

    private sealed record OllamaEmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt);

    private sealed class OllamaEmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; init; }
    }
}
