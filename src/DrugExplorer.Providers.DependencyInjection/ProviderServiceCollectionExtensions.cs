using DrugExplorer.ProviderAbstractions.Interfaces;
using DrugExplorer.Providers.Ollama;
using DrugExplorer.Providers.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DrugExplorer.Providers.DependencyInjection;

public static class ProviderServiceCollectionExtensions
{
    public static IServiceCollection AddDrugExplorerProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var selection = configuration.GetSection("Providers").Get<ProviderSelectionOptions>()
            ?? new ProviderSelectionOptions();

        if (!string.Equals(selection.VectorStore, "Qdrant", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Vector store provider '{selection.VectorStore}' is not supported by this build.");
        }

        if (!string.Equals(selection.Ai, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"AI provider '{selection.Ai}' is not supported by this build.");
        }

        services.AddOptions<QdrantOptions>()
            .Bind(configuration.GetSection("Qdrant"))
            .Validate(options =>
                Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                "Qdrant:BaseUrl must be an absolute HTTP or HTTPS URL.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.CollectionName),
                "Qdrant:CollectionName is required.")
            .Validate(options => options.VectorSize > 0,
                "Qdrant:VectorSize must be greater than zero.")
            .ValidateOnStart();

        services.AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection("Ollama"))
            .Validate(options =>
                Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                "Ollama:BaseUrl must be an absolute HTTP or HTTPS URL.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.EmbeddingModel),
                "Ollama:EmbeddingModel is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ChatModel),
                "Ollama:ChatModel is required.")
            .ValidateOnStart();

        services.AddHttpClient("DrugExplorer.Qdrant", (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<QdrantOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
        });
        services.AddHttpClient("DrugExplorer.Ollama", (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
        });

        services.AddTransient<IVectorStore>(serviceProvider =>
        {
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient("DrugExplorer.Qdrant");
            var options = serviceProvider.GetRequiredService<IOptions<QdrantOptions>>().Value;
            return new QdrantVectorStore(httpClient, options);
        });
        services.AddTransient<IVectorIndexWriter>(serviceProvider =>
        {
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient("DrugExplorer.Qdrant");
            var options = serviceProvider.GetRequiredService<IOptions<QdrantOptions>>().Value;
            return new QdrantVectorIndexWriter(httpClient, options);
        });
        services.AddTransient<IEmbeddingService>(serviceProvider =>
        {
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient("DrugExplorer.Ollama");
            var options = serviceProvider.GetRequiredService<IOptions<OllamaOptions>>().Value;
            return new OllamaEmbeddingService(httpClient, options.EmbeddingModel);
        });
        services.AddTransient<IChatCompletionService>(serviceProvider =>
        {
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient("DrugExplorer.Ollama");
            var options = serviceProvider.GetRequiredService<IOptions<OllamaOptions>>().Value;
            return new OllamaChatCompletionService(httpClient, options.ChatModel);
        });

        return services;
    }
}
