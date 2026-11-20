using System.Text.Json;
using DrugExplorer.Contracts.Indexing;

namespace DrugExplorer.Contracts.Tests;

[TestClass]
public sealed class BuildIndexRequestedTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void SerializesAndDeserializesVersionedContract()
    {
        var request = new BuildIndexRequested
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            RequestedAtUtc = DateTimeOffset.UtcNow,
            DrugKey = "drug-123"
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);

        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(request.MessageId.ToString(), document.RootElement.GetProperty("messageId").GetString());
        Assert.AreEqual(BuildIndexRequested.CurrentSchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());

        var deserialized = JsonSerializer.Deserialize<BuildIndexRequested>(json, JsonOptions);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(request, deserialized);
    }
}
