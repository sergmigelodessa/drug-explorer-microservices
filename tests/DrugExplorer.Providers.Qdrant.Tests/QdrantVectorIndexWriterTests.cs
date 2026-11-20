using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DrugExplorer.ProviderAbstractions.Models;
using DrugExplorer.Providers.Qdrant;

namespace DrugExplorer.Providers.Qdrant.Tests;

[TestClass]
public sealed class QdrantVectorIndexWriterTests
{
    [TestMethod]
    public async Task EnsureIndexAsyncCreatesCollectionWhenMissing()
    {
        JsonElement createBody = default;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            createBody = await request.Content!.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = CreateClient(handler);
        var writer = CreateWriter(httpClient);

        await writer.EnsureIndexAsync();

        Assert.AreEqual("PUT", handler.RequestMethods[1].Method);
        Assert.AreEqual("/collections/drug_chunks", handler.RequestMethods[1].Path);
        Assert.AreEqual(768, createBody.GetProperty("vectors").GetProperty("size").GetInt32());
        Assert.AreEqual("Cosine", createBody.GetProperty("vectors").GetProperty("distance").GetString());
    }

    [TestMethod]
    public async Task EnsureIndexAsyncRejectsCollectionWithWrongVectorSize()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    result = new
                    {
                        config = new
                        {
                            @params = new { vectors = new { size = 384, distance = "Cosine" } }
                        }
                    }
                })
            }));
        using var httpClient = CreateClient(handler);
        var writer = CreateWriter(httpClient);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => writer.EnsureIndexAsync());
    }

    [TestMethod]
    public async Task GetExistingIdsAsyncReturnsIds()
    {
        var existingId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    result = new[] { new { id = existingId.ToString() } }
                })
            }));
        using var httpClient = CreateClient(handler);
        var writer = CreateWriter(httpClient);

        var result = await writer.GetExistingIdsAsync([existingId, Guid.NewGuid()]);

        Assert.HasCount(1, result);
        Assert.Contains(existingId, result);
        Assert.AreEqual("/collections/drug_chunks/points", handler.RequestMethods.Single().Path);
    }

    [TestMethod]
    public async Task GetExistingIdsAsyncRejectsInvalidId()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    result = new[] { new { id = "not-a-guid" } }
                })
            }));
        using var httpClient = CreateClient(handler);
        var writer = CreateWriter(httpClient);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => writer.GetExistingIdsAsync([Guid.NewGuid()]));
    }

    [TestMethod]
    public async Task GetExistingIdsAsyncDoesNotSendRequestForEmptyInput()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("No request should be sent."));
        using var httpClient = CreateClient(handler);
        var writer = CreateWriter(httpClient);

        var result = await writer.GetExistingIdsAsync([]);

        Assert.IsEmpty(result);
        Assert.IsEmpty(handler.RequestMethods);
    }

    [TestMethod]
    public async Task UpsertAsyncMapsDocumentAndWaitsForCompletion()
    {
        JsonElement requestBody = default;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = CreateClient(handler);
        var writer = CreateWriter(httpClient);
        var id = Guid.NewGuid();
        var document = new VectorDocument(
            id,
            "drug-123",
            "Brand",
            "Generic",
            "Indications",
            "Label text",
            [0.1f, -0.2f]);

        await writer.UpsertAsync([document]);

        Assert.AreEqual("PUT", handler.RequestMethods.Single().Method);
        Assert.AreEqual("/collections/drug_chunks/points", handler.RequestMethods.Single().Path);
        Assert.AreEqual("wait=true", handler.RequestMethods.Single().Query);
        var point = requestBody.GetProperty("points")[0];
        Assert.AreEqual(id.ToString(), point.GetProperty("id").GetString());
        CollectionAssert.AreEqual(new[] { 0.1f, -0.2f }, point.GetProperty("vector").EnumerateArray().Select(value => value.GetSingle()).ToArray());
        var payload = point.GetProperty("payload");
        Assert.AreEqual("drug-123", payload.GetProperty("drugKey").GetString());
        Assert.AreEqual("Label text", payload.GetProperty("chunkText").GetString());
    }

    private static HttpClient CreateClient(StubHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://localhost:6333/") };

    private static QdrantVectorIndexWriter CreateWriter(HttpClient httpClient) =>
        new(httpClient, new QdrantOptions());

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        public List<RequestInfo> RequestMethods { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestMethods.Add(new RequestInfo(
                request.Method.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.RequestUri?.Query.TrimStart('?') ?? string.Empty));
            return await sendAsync(request, cancellationToken);
        }
    }

    private sealed record RequestInfo(string Method, string Path, string Query);
}
