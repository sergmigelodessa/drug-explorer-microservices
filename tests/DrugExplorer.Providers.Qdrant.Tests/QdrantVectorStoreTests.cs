using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DrugExplorer.Providers.Qdrant;

namespace DrugExplorer.Providers.Qdrant.Tests;

[TestClass]
public sealed class QdrantVectorStoreTests
{
    [TestMethod]
    public async Task CountAsyncRequestsExactCount()
    {
        JsonElement requestBody = default;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { result = new { count = 42 } })
            };
        });
        using var httpClient = CreateClient(handler);
        var store = CreateStore(httpClient);

        var count = await store.CountAsync();

        Assert.AreEqual(42, count);
        Assert.AreEqual("/collections/drug_chunks/points/count", handler.Requests.Single().Path);
        Assert.IsTrue(requestBody.GetProperty("exact").GetBoolean());
    }

    [TestMethod]
    public async Task CountAsyncReturnsZeroWhenCollectionIsMissing()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var httpClient = CreateClient(handler);
        var store = CreateStore(httpClient);

        var count = await store.CountAsync();

        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public async Task SearchAsyncSendsQueryAndMapsResultPayload()
    {
        var id = Guid.NewGuid();
        JsonElement requestBody = default;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    result = new[]
                    {
                        new
                        {
                            id = id.ToString(),
                            score = 0.93,
                            payload = new
                            {
                                drugKey = "drug-123",
                                brandName = "Brand",
                                genericName = "Generic",
                                chunkType = "Indications",
                                chunkText = "Label text"
                            }
                        }
                    }
                })
            };
        });
        using var httpClient = CreateClient(handler);
        var store = CreateStore(httpClient);

        var results = await store.SearchAsync([0.25f, -0.5f], topK: 5);

        Assert.AreEqual("/collections/drug_chunks/points/search", handler.Requests.Single().Path);
        CollectionAssert.AreEqual(
            new[] { 0.25f, -0.5f },
            requestBody.GetProperty("vector").EnumerateArray().Select(value => value.GetSingle()).ToArray());
        Assert.AreEqual(5, requestBody.GetProperty("limit").GetInt32());
        Assert.IsTrue(requestBody.GetProperty("with_payload").GetBoolean());
        Assert.IsFalse(requestBody.GetProperty("with_vector").GetBoolean());

        Assert.HasCount(1, results);
        Assert.AreEqual(id, results[0].Id);
        Assert.AreEqual("drug-123", results[0].DrugKey);
        Assert.AreEqual("Label text", results[0].ChunkText);
        Assert.AreEqual(0.93, results[0].Similarity);
    }

    [TestMethod]
    public async Task SearchAsyncRejectsPointWithInvalidId()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    result = new[]
                    {
                        new
                        {
                            id = "not-a-guid",
                            score = 0.93,
                            payload = new
                            {
                                drugKey = "drug-123",
                                brandName = "Brand",
                                genericName = "Generic",
                                chunkType = "Indications",
                                chunkText = "Label text"
                            }
                        }
                    }
                })
            }));
        using var httpClient = CreateClient(handler);
        var store = CreateStore(httpClient);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => store.SearchAsync([0.25f, -0.5f], topK: 5));
    }

    [TestMethod]
    public async Task SearchAsyncReturnsEmptyWhenCollectionIsMissing()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var httpClient = CreateClient(handler);
        var store = CreateStore(httpClient);

        var results = await store.SearchAsync([0.25f, -0.5f], topK: 5);

        Assert.IsEmpty(results);
    }

    private static HttpClient CreateClient(StubHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://localhost:6333/") };

    private static QdrantVectorStore CreateStore(HttpClient httpClient) =>
        new(httpClient, new QdrantOptions());

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        public List<RequestInfo> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RequestInfo(
                request.Method.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty));
            return await sendAsync(request, cancellationToken);
        }
    }

    private sealed record RequestInfo(string Method, string Path);
}
