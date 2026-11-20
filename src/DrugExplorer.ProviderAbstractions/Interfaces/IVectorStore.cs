using DrugExplorer.ProviderAbstractions.Models;

namespace DrugExplorer.ProviderAbstractions.Interfaces;

public interface IVectorStore
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken = default);
}
