namespace DrugExplorer.ProviderAbstractions.Models;

public sealed record VectorSearchHit(
    Guid Id,
    string DrugKey,
    string BrandName,
    string GenericName,
    string ChunkType,
    string ChunkText,
    double Similarity);
