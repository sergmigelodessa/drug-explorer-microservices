using DrugExplorer.ProviderAbstractions.Interfaces;
using DrugExplorer.Providers.DependencyInjection;
using DrugExplorer.Providers.Ollama;
using DrugExplorer.Providers.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DrugExplorer.Providers.DependencyInjection.Tests;

[TestClass]
public sealed class ProviderServiceCollectionExtensionsTests
{
    [TestMethod]
    public void AddDrugExplorerProvidersRegistersSelectedLocalProviders()
    {
        var configuration = CreateConfiguration();
        var services = new ServiceCollection();
        services.AddDrugExplorerProviders(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.IsInstanceOfType(provider.GetRequiredService<IEmbeddingService>(), typeof(OllamaEmbeddingService));
        Assert.IsInstanceOfType(provider.GetRequiredService<IChatCompletionService>(), typeof(OllamaChatCompletionService));
        Assert.IsInstanceOfType(provider.GetRequiredService<IVectorStore>(), typeof(QdrantVectorStore));
        Assert.IsInstanceOfType(provider.GetRequiredService<IVectorIndexWriter>(), typeof(QdrantVectorIndexWriter));
    }

    [TestMethod]
    public void AddDrugExplorerProvidersRejectsUnsupportedAiProvider()
    {
        var configuration = CreateConfiguration(("Providers:Ai", "AzureOpenAI"));
        var services = new ServiceCollection();

        Assert.ThrowsExactly<NotSupportedException>(
            () => services.AddDrugExplorerProviders(configuration));
    }

    [TestMethod]
    public void AddDrugExplorerProvidersRejectsUnsupportedVectorStore()
    {
        var configuration = CreateConfiguration(("Providers:VectorStore", "AzureAISearch"));
        var services = new ServiceCollection();

        Assert.ThrowsExactly<NotSupportedException>(
            () => services.AddDrugExplorerProviders(configuration));
    }

    private static IConfiguration CreateConfiguration(params (string Key, string Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Providers:VectorStore"] = "Qdrant",
            ["Providers:Ai"] = "Ollama",
            ["Qdrant:BaseUrl"] = "http://localhost:6333/",
            ["Qdrant:CollectionName"] = "drug_chunks",
            ["Qdrant:VectorSize"] = "768",
            ["Ollama:BaseUrl"] = "http://localhost:11434/",
            ["Ollama:EmbeddingModel"] = "embedding-model",
            ["Ollama:ChatModel"] = "chat-model"
        };

        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }
}
