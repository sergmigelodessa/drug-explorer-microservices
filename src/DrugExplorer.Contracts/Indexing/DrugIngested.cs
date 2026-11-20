namespace DrugExplorer.Contracts.Indexing;

public sealed record DrugIngested
{
    public const int CurrentSchemaVersion = 1;

    public required Guid MessageId { get; init; }

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid CorrelationId { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public required string DrugKey { get; init; }
}
