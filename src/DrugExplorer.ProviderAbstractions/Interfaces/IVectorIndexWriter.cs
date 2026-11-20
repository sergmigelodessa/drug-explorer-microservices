using DrugExplorer.ProviderAbstractions.Models;

namespace DrugExplorer.ProviderAbstractions.Interfaces;

public interface IVectorIndexWriter
{
    Task EnsureIndexAsync(CancellationToken cancellationToken = default);

    Task<HashSet<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        IReadOnlyCollection<VectorDocument> documents,
        CancellationToken cancellationToken = default);
}
