namespace DrugExplorer.Providers.DependencyInjection;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434/";

    public string EmbeddingModel { get; set; } = string.Empty;

    public string ChatModel { get; set; } = string.Empty;
}
