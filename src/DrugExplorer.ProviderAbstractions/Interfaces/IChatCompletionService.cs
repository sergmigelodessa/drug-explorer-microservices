namespace DrugExplorer.ProviderAbstractions.Interfaces;

public interface IChatCompletionService
{
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default);
}
