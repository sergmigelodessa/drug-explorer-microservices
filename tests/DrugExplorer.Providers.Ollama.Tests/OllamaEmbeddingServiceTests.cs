using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DrugExplorer.Providers.Ollama;

namespace DrugExplorer.Providers.Ollama.Tests;

[TestClass]
public sealed class OllamaEmbeddingServiceTests
{
    [TestMethod]
    public async Task EmbedAsyncSendsModelAndPromptAndReturnsEmbedding()
    {
        HttpMethod? requestMethod = null;
        string? requestPath = null;
        string? requestedModel = null;
        string? requestedPrompt = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestMethod = request.Method;
            requestPath = request.RequestUri?.AbsolutePath;
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken);
            requestedModel = body.GetProperty("model").GetString();
            requestedPrompt = body.GetProperty("prompt").GetString();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { embedding = new[] { 0.1f, -0.2f } })
            };
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var service = new OllamaEmbeddingService(httpClient, "nomic-embed-text");

        var embedding = await service.EmbedAsync("drug label text");

        Assert.AreEqual(HttpMethod.Post, requestMethod);
        Assert.AreEqual("/api/embeddings", requestPath);
        Assert.AreEqual("nomic-embed-text", requestedModel);
        Assert.AreEqual("drug label text", requestedPrompt);
        CollectionAssert.AreEqual(new[] { 0.1f, -0.2f }, embedding);
    }

    [TestMethod]
    public async Task EmbedAsyncThrowsWhenResponseHasNoEmbedding()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { embedding = Array.Empty<float>() })
            }));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var service = new OllamaEmbeddingService(httpClient, "nomic-embed-text");

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => service.EmbedAsync("drug label text"));
    }

    [TestMethod]
    public async Task EmbedAsyncSurfacesHttpFailures()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var service = new OllamaEmbeddingService(httpClient, "nomic-embed-text");

        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => service.EmbedAsync("drug label text"));
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            sendAsync(request, cancellationToken);
    }
}
