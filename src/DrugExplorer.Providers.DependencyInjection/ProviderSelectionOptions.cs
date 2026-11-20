namespace DrugExplorer.Providers.DependencyInjection;

public sealed class ProviderSelectionOptions
{
    public string VectorStore { get; set; } = "Qdrant";

    public string Ai { get; set; } = "Ollama";
}
