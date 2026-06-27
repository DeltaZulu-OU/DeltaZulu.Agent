using DeltaZulu.Agent.Domain.Delivery;
using DeltaZulu.Agent.Domain.Events;
using DeltaZulu.Agent.Inputs.MessagePack;
using DeltaZulu.Agent.Outputs.MessagePack;
using DeltaZulu.Agent.Shared.MessagePack;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class MessagePackTests
{
    [TestMethod]
    public void MessagePackPayloadSerializer_RoundtripsDeliveryBatch()
    {
        var serializer = new MessagePackPayloadSerializer();
        var batch = CreateBatch();

        var payload = serializer.Serialize(batch);
        var decoded = serializer.Deserialize<DeliveryBatch>(payload);

        Assert.IsNotNull(decoded);
        Assert.AreEqual(batch.BatchId, decoded.BatchId);
        Assert.AreEqual(1, decoded.Records.Count);
        Assert.AreEqual("agent-a", decoded.Records[0].AgentId);
        Assert.AreEqual("value", decoded.Records[0].Record.Event["field"]?.ToString());
    }

    [TestMethod]
    public void MessagePackInputAndOutputFolders_ShareCodecWithoutReferencingEachOther()
    {
        var encoder = new MessagePackDeliveryBatchEncoder();
        var decoder = new MessagePackDeliveryBatchDecoder();
        var batch = CreateBatch();

        var payload = encoder.Encode(batch);
        var decoded = decoder.Decode(payload);

        Assert.IsNotNull(decoded);
        Assert.AreEqual(batch.BatchId, decoded.BatchId);
        Assert.AreEqual(batch.Records[0].RecordId, decoded.Records[0].RecordId);
        Assert.AreEqual("linux", decoded.Records[0].Record.Metadata["platform"]?.ToString());
    }

    private static DeliveryBatch CreateBatch() => new()
    {
        BatchId = "batch-1",
        CreatedAt = DateTimeOffset.Parse("2026-06-27T00:00:00Z"),
        Records =
        [
            new DeliveryRecord
            {
                AgentId = "agent-a",
                SourceId = "syslog:auth",
                ProfileId = "linux.auth",
                RecordId = "record-1",
                CreatedAt = DateTimeOffset.Parse("2026-06-27T00:00:01Z"),
                Record = new ResourceOutputRecord
                {
                    Metadata = new Dictionary<string, object?>
                    {
                        ["platform"] = "linux"
                    },
                    Event = new Dictionary<string, object?>
                    {
                        ["field"] = "value"
                    }
                }
            }
        ]
    };
}
