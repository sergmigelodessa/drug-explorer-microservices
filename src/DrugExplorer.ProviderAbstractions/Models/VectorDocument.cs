namespace DrugExplorer.ProviderAbstractions.Models;

public sealed record VectorDocument(
    Guid Id,
    string DrugKey,
    string BrandName,
    string GenericName,
    string ChunkType,
    string ChunkText,
    float[] Vector);
