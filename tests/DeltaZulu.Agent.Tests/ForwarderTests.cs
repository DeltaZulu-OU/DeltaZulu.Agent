using System.Buffers.Binary;
using System.Text.Json;
using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Agent.Core.Observability;
using DeltaZulu.Agent.Forwarder;
using DeltaZulu.Buffer.Chunks;
using DeltaZulu.Buffer.Dispatch;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ForwarderTests
{
    [TestMethod]
    public void DeliveryRecord_FromResourceOutput_PreservesAgentSourceProfileAndRecordIdentity()
    {
        var output = new ResourceOutputRecord
        {
            Metadata = new Dictionary<string, object?>
            {
                ["collectorId"] = "agent-01",
                ["sourceType"] = "Syslog",
                ["sourceName"] = "auth.log",
                ["profileId"] = "linux.sshd"
            },
            Event = new Dictionary<string, object?>
            {
                ["RecordId"] = 42,
                ["Message"] = "accepted publickey"
            }
        };

        var delivery = DeliveryRecord.FromResourceOutput(output);

        Assert.AreEqual("agent-01", delivery.AgentId);
        Assert.AreEqual("Syslog:auth.log", delivery.SourceId);
        Assert.AreEqual("linux.sshd", delivery.ProfileId);
        Assert.AreEqual("42", delivery.RecordId);
        Assert.AreSame(output, delivery.Record);
    }

    [TestMethod]
    public async Task ForwarderChunkSender_DecodesChunkAndSendsDeliveryBatch()
    {
        using var directory = new TemporaryDirectory();
        var chunkId = ChunkId.NewChunkId();
        var record = new DeliveryRecord
        {
            AgentId = "agent-01",
            SourceId = "Syslog:auth.log",
            ProfileId = "linux.sshd",
            RecordId = "42",
            CreatedAt = DateTimeOffset.UtcNow,
            Record = new ResourceOutputRecord
            {
                Metadata = new Dictionary<string, object?> { ["collectorId"] = "agent-01" },
                Event = new Dictionary<string, object?> { ["Message"] = "hello" }
            }
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(record, TestJson.Options);
        var chunkPath = Path.Combine(directory.Path, $"{chunkId}.chunk");
        var metadataPath = Path.Combine(directory.Path, $"{chunkId}.meta.json");
        await File.WriteAllBytesAsync(chunkPath, CreateChunkBytes(payload));
        await File.WriteAllTextAsync(metadataPath, "{}");

        var transport = new CapturingTransport();
        var sender = new ForwarderChunkSender(transport);
        var result = await sender.SendAsync(new StoredChunk
        {
            Id = chunkId,
            ChunkFilePath = chunkPath,
            MetadataFilePath = metadataPath,
            Metadata = new ChunkMetadata
            {
                ChunkId = chunkId.Value,
                CreatedUtc = DateTimeOffset.UtcNow,
                SealedUtc = DateTimeOffset.UtcNow,
                RecordCount = 1,
                PayloadBytes = payload.Length,
                Checksum = "sha256:test"
            }
        });

        Assert.AreEqual(ChunkSendStatus.Success, result.Status);
        Assert.IsNotNull(transport.Batch);
        Assert.AreEqual(chunkId.Value, transport.Batch!.BatchId);
        Assert.AreEqual(1, transport.Batch.Records.Count);
        Assert.AreEqual("42", transport.Batch.Records[0].RecordId);
    }

    [TestMethod]
    public void BufferedForwarderSink_HealthSnapshot_ReportsBufferAndForwarderCounters()
    {
        using var directory = new TemporaryDirectory();
        var transport = new CapturingTransport();
        var options = new DeltaZulu.Buffer.Configuration.DeltaZuluBufferOptions
        {
            StoragePath = directory.Path,
            MaxChunkRecords = 1,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        using var sink = new BufferedForwarderSink(options, transport);
        sink.OnNext(new ResourceOutputRecord
        {
            Metadata = new Dictionary<string, object?>
            {
                ["collectorId"] = "agent-01",
                ["sourceType"] = "Syslog",
                ["sourceName"] = "auth.log",
                ["profileId"] = "linux.sshd"
            },
            Event = new Dictionary<string, object?>
            {
                ["RecordId"] = "42",
                ["Message"] = "accepted publickey"
            }
        });
        sink.OnCompleted();

        var health = sink.GetHealthSnapshot();

        Assert.AreEqual(1, health.Buffer.RecordsAcceptedTotal);
        Assert.IsGreaterThanOrEqualTo(1, health.BatchesSentTotal);
        Assert.IsGreaterThanOrEqualTo(1, health.BatchesAcknowledgedTotal);
        Assert.AreEqual(0, health.BatchesDeadLetteredTotal);
        Assert.IsNotNull(health.LastForwarderActivityUtc);

        var observation = sink.GetHealthOutputRecord(new CollectorObservationMetadata
        {
            AgentId = "agent-01",
            HostId = "host-01"
        });

        Assert.AreEqual(ForwarderHealthObservation.RecordKind, observation.Metadata["recordKind"]);
        Assert.AreEqual(1L, observation.Event["recordsAcceptedTotal"]);
        Assert.AreEqual(0L, observation.Event["batchesDeadLetteredTotal"]);
    }

    private static byte[] CreateChunkBytes(byte[] payload)
    {
        var data = new byte[ChunkFormat.HeaderSize + ChunkFormat.RecordLengthSize + payload.Length + ChunkFormat.FooterSize];
        ChunkFormat.Magic.CopyTo(data.AsSpan(ChunkFormat.MagicOffset));
        data[ChunkFormat.VersionOffset] = ChunkFormat.Version;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(ChunkFormat.HeaderSize, ChunkFormat.RecordLengthSize), payload.Length);
        payload.CopyTo(data.AsSpan(ChunkFormat.HeaderSize + ChunkFormat.RecordLengthSize));
        ChunkFormat.FooterMagic.CopyTo(data.AsSpan(data.Length - 4));
        return data;
    }

    private sealed class CapturingTransport : IForwarderTransport
    {
        public DeliveryBatch? Batch { get; private set; }

        public ValueTask<DeliveryAck> SendAsync(DeliveryBatch batch, CancellationToken cancellationToken = default)
        {
            Batch = batch;
            return ValueTask.FromResult(new DeliveryAck
            {
                BatchId = batch.BatchId,
                Accepted = true
            });
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "deltazulu-forwarder-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private static class TestJson
    {
        public static JsonSerializerOptions Options { get; } = DeltaZulu.Agent.Outputs.Ndjson.NdjsonSerializerOptions.CreateDefault();
    }
}
