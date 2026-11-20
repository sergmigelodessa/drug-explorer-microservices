namespace DrugExplorer.Contracts.Indexing;

public sealed record BuildIndexRequested
{
    public const int CurrentSchemaVersion = 1;

    public required Guid MessageId { get; init; }

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid CorrelationId { get; init; }

    public required Guid JobId { get; init; }

    public required DateTimeOffset RequestedAtUtc { get; init; }

    public string? DrugKey { get; init; }
}
