using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DrugExplorer.ProviderAbstractions.Interfaces;

namespace DrugExplorer.Providers.Ollama;

public sealed class OllamaChatCompletionService : IChatCompletionService
{
    private const string GenerateEndpoint = "api/generate";

    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaChatCompletionService(HttpClient httpClient, string model)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _httpClient = httpClient;
        _model = model;
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        using var response = await _httpClient.PostAsJsonAsync(
            GenerateEndpoint,
            new OllamaGenerateRequest(_model, prompt, Stream: false),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(
            cancellationToken: cancellationToken);
        var completion = result?.Response?.Trim();

        if (string.IsNullOrWhiteSpace(completion))
        {
            throw new InvalidDataException("Ollama returned an empty completion.");
        }

        return completion;
    }

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("response")]
        public string? Response { get; init; }
    }
}
