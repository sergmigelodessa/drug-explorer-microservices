namespace DrugExplorer.Providers.Qdrant;

public sealed class QdrantOptions
{
    public string BaseUrl { get; set; } = "http://localhost:6333/";

    public string CollectionName { get; set; } = "drug_chunks";

    public int VectorSize { get; set; } = 768;
}
