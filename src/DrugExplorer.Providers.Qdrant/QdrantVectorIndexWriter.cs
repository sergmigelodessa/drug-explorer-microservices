using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DrugExplorer.ProviderAbstractions.Interfaces;
using DrugExplorer.ProviderAbstractions.Models;

namespace DrugExplorer.Providers.Qdrant;

public sealed class QdrantVectorIndexWriter : IVectorIndexWriter
{
    private const string Distance = "Cosine";

    private readonly HttpClient _httpClient;
    private readonly QdrantOptions _options;
    private readonly string _collectionPath;

    public QdrantVectorIndexWriter(HttpClient httpClient, QdrantOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CollectionName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.VectorSize);

        _httpClient = httpClient;
        _options = options;
        _collectionPath = $"collections/{Uri.EscapeDataString(options.CollectionName)}";
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(_collectionPath, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            using var createResponse = await _httpClient.PutAsJsonAsync(
                _collectionPath,
                new { vectors = new { size = _options.VectorSize, distance = Distance } },
                cancellationToken);
            createResponse.EnsureSuccessStatusCode();
            return;
        }

        response.EnsureSuccessStatusCode();

        var node = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: cancellationToken);
        var vectors = node?["result"]?["config"]?["params"]?["vectors"];
        var size = vectors?["size"]?.GetValue<int>();
        var distance = vectors?["distance"]?.GetValue<string>();

        if (size != _options.VectorSize || !string.Equals(distance, Distance, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Qdrant collection '{_options.CollectionName}' has size={size}, distance={distance}; " +
                $"expected size={_options.VectorSize}, distance={Distance}.");
        }
    }

    public async Task<HashSet<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return [];
        }

        using var response = await _httpClient.PostAsJsonAsync(
            $"{_collectionPath}/points",
            new { ids, with_payload = false, with_vector = false },
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RetrieveResponse>(
            cancellationToken: cancellationToken);

        var points = result?.Points
            ?? throw new InvalidDataException("Qdrant returned no retrieved points collection.");

        return points.Select(point =>
        {
            if (!Guid.TryParse(point.Id, out var id))
            {
                throw new InvalidDataException("Qdrant returned a point with an invalid ID.");
            }

            return id;
        }).ToHashSet();
    }

    public async Task UpsertAsync(
        IReadOnlyCollection<VectorDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0)
        {
            return;
        }

        var request = new UpsertRequest(documents.Select(document => new QdrantPoint(
            document.Id,
            document.Vector,
            new PointPayload(
                document.DrugKey,
                document.BrandName,
                document.GenericName,
                document.ChunkType,
                document.ChunkText))).ToArray());

        using var response = await _httpClient.PutAsJsonAsync(
            $"{_collectionPath}/points?wait=true",
            request,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed class RetrieveResponse
    {
        [JsonPropertyName("result")]
        public List<RetrievedPoint>? Points { get; init; }
    }

    private sealed class RetrievedPoint
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }
    }

    private sealed record UpsertRequest(
        [property: JsonPropertyName("points")] IReadOnlyCollection<QdrantPoint> Points);

    private sealed record QdrantPoint(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("vector")] float[] Vector,
        [property: JsonPropertyName("payload")] PointPayload Payload);

    private sealed record PointPayload(
        string DrugKey,
        string BrandName,
        string GenericName,
        string ChunkType,
        string ChunkText);
}
