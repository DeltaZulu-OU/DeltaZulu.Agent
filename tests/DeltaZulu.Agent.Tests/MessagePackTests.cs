using DeltaZulu.Pipeline.Core.Delivery;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Inputs.MessagePack;
using DeltaZulu.Pipeline.Outputs.MessagePack;
using DeltaZulu.Pipeline.Core.MessagePack;

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
        Assert.HasCount(1, decoded.Records);
        Assert.AreEqual("agent-a", decoded.Records[0].AgentId);
        Assert.AreEqual("value", decoded.Records[0].Record.Event["field"]?.ToString());
    }

    [TestMethod]
    public void MessagePackPayloadSerializer_PreservesObjectDictionaryPrimitiveValues()
    {
        var serializer = new MessagePackPayloadSerializer();
        var batch = CreateBatch();
        var record = batch.Records[0] with
        {
            Record = batch.Records[0].Record with
            {
                Metadata = new Dictionary<string, object?>
                {
                    ["collectorId"] = "agent-a",
                    ["sourceType"] = "WindowsEventLog",
                    ["ingestedAt"] = DateTimeOffset.Parse("2026-06-27T00:00:02Z"),
                    ["rawPreserved"] = true
                },
                Event = new Dictionary<string, object?>
                {
                    ["ProviderName"] = "Microsoft-Windows-Security-Auditing",
                    ["EventId"] = 4688,
                    ["RecordId"] = 11320633L,
                    ["TimeCreated"] = DateTimeOffset.Parse("2026-06-27T00:00:03Z"),
                    ["EventData"] = new Dictionary<string, object?>
                    {
                        ["SubjectUserName"] = "demo-user",
                        ["NewProcessId"] = 1234,
                        ["UnsupportedObject"] = new Version(1, 2, 3)
                    }
                }
            }
        };

        batch = batch with { Records = [record] };

        var payload = serializer.Serialize(batch);
        var decoded = serializer.Deserialize<DeliveryBatch>(payload);

        Assert.IsNotNull(decoded);
        var decodedRecord = decoded.Records[0].Record;
        Assert.AreEqual("agent-a", decodedRecord.Metadata["collectorId"]);
        Assert.AreEqual("Microsoft-Windows-Security-Auditing", decodedRecord.Event["ProviderName"]);
        Assert.AreEqual(4688, Convert.ToInt32(decodedRecord.Event["EventId"]));
        Assert.AreEqual(11320633L, Convert.ToInt64(decodedRecord.Event["RecordId"]));

        Assert.IsInstanceOfType(decodedRecord.Event["EventData"], typeof(IReadOnlyDictionary<string, object?>));
        var eventData = (IReadOnlyDictionary<string, object?>)decodedRecord.Event["EventData"]!;
        Assert.AreEqual("demo-user", eventData["SubjectUserName"]);
        Assert.AreEqual(1234, Convert.ToInt32(eventData["NewProcessId"]));
        Assert.AreEqual("1.2.3", eventData["UnsupportedObject"]);
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
