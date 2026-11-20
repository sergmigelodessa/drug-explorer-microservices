using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DrugExplorer.Providers.Ollama;

namespace DrugExplorer.Providers.Ollama.Tests;

[TestClass]
public sealed class OllamaChatCompletionServiceTests
{
    [TestMethod]
    public async Task CompleteAsyncSendsNonStreamingPromptAndReturnsTrimmedResponse()
    {
        HttpMethod? requestMethod = null;
        string? requestPath = null;
        string? requestedModel = null;
        string? requestedPrompt = null;
        bool? requestedStream = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestMethod = request.Method;
            requestPath = request.RequestUri?.AbsolutePath;
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken);
            requestedModel = body.GetProperty("model").GetString();
            requestedPrompt = body.GetProperty("prompt").GetString();
            requestedStream = body.GetProperty("stream").GetBoolean();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = "  grounded answer  " })
            };
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var service = new OllamaChatCompletionService(httpClient, "qwen2.5:1.5b");

        var completion = await service.CompleteAsync("Answer from the supplied context.");

        Assert.AreEqual(HttpMethod.Post, requestMethod);
        Assert.AreEqual("/api/generate", requestPath);
        Assert.AreEqual("qwen2.5:1.5b", requestedModel);
        Assert.AreEqual("Answer from the supplied context.", requestedPrompt);
        Assert.IsFalse(requestedStream);
        Assert.AreEqual("grounded answer", completion);
    }

    [TestMethod]
    public async Task CompleteAsyncThrowsWhenResponseIsEmpty()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = "  " })
            }));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var service = new OllamaChatCompletionService(httpClient, "qwen2.5:1.5b");

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => service.CompleteAsync("Answer from the supplied context."));
    }

    [TestMethod]
    public async Task CompleteAsyncSurfacesHttpFailures()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var service = new OllamaChatCompletionService(httpClient, "qwen2.5:1.5b");

        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => service.CompleteAsync("Answer from the supplied context."));
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
