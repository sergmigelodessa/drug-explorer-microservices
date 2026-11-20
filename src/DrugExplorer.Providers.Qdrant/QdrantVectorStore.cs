using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DrugExplorer.ProviderAbstractions.Interfaces;
using DrugExplorer.ProviderAbstractions.Models;

namespace DrugExplorer.Providers.Qdrant;

public sealed class QdrantVectorStore : IVectorStore
{
    private readonly HttpClient _httpClient;
    private readonly string _collectionPath;

    public QdrantVectorStore(HttpClient httpClient, QdrantOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CollectionName);

        _httpClient = httpClient;
        _collectionPath = $"collections/{Uri.EscapeDataString(options.CollectionName)}";
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"{_collectionPath}/points/count",
            new { exact = true },
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return 0;
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CountResponse>(
            cancellationToken: cancellationToken);

        var count = result?.Count;
        if (count is null or < 0)
        {
            throw new InvalidDataException("Qdrant returned no point count.");
        }

        return count.Value;
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        if (queryVector.Length == 0)
        {
            throw new ArgumentException("Query vector cannot be empty.", nameof(queryVector));
        }

        using var response = await _httpClient.PostAsJsonAsync(
            $"{_collectionPath}/points/search",
            new { vector = queryVector, limit = topK, with_payload = true, with_vector = false },
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>(
            cancellationToken: cancellationToken);

        if (result?.Points is null)
        {
            throw new InvalidDataException("Qdrant returned no search results collection.");
        }

        return result.Points.Select(MapHit).ToArray();
    }

    private static VectorSearchHit MapHit(ScoredPoint point)
    {
        if (!Guid.TryParse(point.Id, out var id))
        {
            throw new InvalidDataException("Qdrant returned a point with an invalid ID.");
        }

        var payload = point.Payload
            ?? throw new InvalidDataException($"Qdrant point '{id}' has no payload.");

        return new VectorSearchHit(
            id,
            payload.DrugKey ?? throw new InvalidDataException($"Qdrant point '{id}' has no drug key."),
            payload.BrandName ?? throw new InvalidDataException($"Qdrant point '{id}' has no brand name."),
            payload.GenericName ?? throw new InvalidDataException($"Qdrant point '{id}' has no generic name."),
            payload.ChunkType ?? throw new InvalidDataException($"Qdrant point '{id}' has no chunk type."),
            payload.ChunkText ?? throw new InvalidDataException($"Qdrant point '{id}' has no chunk text."),
            point.Score);
    }

    private sealed class CountResponse
    {
        [JsonPropertyName("result")]
        public CountResult? Result { get; init; }

        public int? Count => Result?.Count;
    }

    private sealed class CountResult
    {
        [JsonPropertyName("count")]
        public int? Count { get; init; }
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("result")]
        public List<ScoredPoint>? Points { get; init; }
    }

    private sealed class ScoredPoint
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("score")]
        public double Score { get; init; }

        [JsonPropertyName("payload")]
        public PointPayload? Payload { get; init; }
    }

    private sealed class PointPayload
    {
        [JsonPropertyName("drugKey")]
        public string? DrugKey { get; init; }

        [JsonPropertyName("brandName")]
        public string? BrandName { get; init; }

        [JsonPropertyName("genericName")]
        public string? GenericName { get; init; }

        [JsonPropertyName("chunkType")]
        public string? ChunkType { get; init; }

        [JsonPropertyName("chunkText")]
        public string? ChunkText { get; init; }
    }
}
