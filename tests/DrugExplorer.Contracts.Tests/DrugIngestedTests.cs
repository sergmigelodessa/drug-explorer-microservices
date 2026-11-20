using System.Text.Json;
using DrugExplorer.Contracts.Indexing;

namespace DrugExplorer.Contracts.Tests;

[TestClass]
public sealed class DrugIngestedTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void SerializesAndDeserializesVersionedContract()
    {
        var message = new DrugIngested
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            DrugKey = "drug-123"
        };

        var json = JsonSerializer.Serialize(message, JsonOptions);

        using var document = JsonDocument.Parse(json);
        Assert.AreEqual(message.DrugKey, document.RootElement.GetProperty("drugKey").GetString());
        Assert.AreEqual(DrugIngested.CurrentSchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());

        var deserialized = JsonSerializer.Deserialize<DrugIngested>(json, JsonOptions);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(message, deserialized);
    }
}
