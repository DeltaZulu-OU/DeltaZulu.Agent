using System.Buffers.Binary;
using System.Text.Json;
using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Agent.Core.Observability;
using DeltaZulu.Agent.Forwarder;
using DeltaZulu.Buffer.Abstractions;
using DeltaZulu.Buffer.Chunks;
using DeltaZulu.Buffer.Dispatch;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ForwarderTests
{
    [TestMethod]
    public void BufferedForwarderSink_HealthSnapshot_ReportsBufferAndForwarderCounters()
    {
        using var directory = new TemporaryDirectory();
        var transport = new CapturingTransport();
        var options = new Buffer.Configuration.DeltaZuluBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 1,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        using var sink = new BufferedForwarderSink(options, transport);
        sink.OnNext(new ResourceOutputRecord {
            Metadata = new Dictionary<string, object?> {
                ["collectorId"] = "agent-01",
                ["sourceType"] = "Syslog",
                ["sourceName"] = "auth.log",
                ["profileId"] = "linux.sshd"
            },
            Event = new Dictionary<string, object?> {
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

        var observation = sink.GetHealthOutputRecord(new CollectorObservationMetadata {
            AgentId = "agent-01",
            HostId = "host-01"
        });

        Assert.AreEqual(ForwarderHealthObservation.RecordKind, observation.Metadata["recordKind"]);
        Assert.AreEqual(1L, observation.Event["recordsAcceptedTotal"]);
        Assert.AreEqual(0L, observation.Event["batchesDeadLetteredTotal"]);
    }

    [TestMethod]
    public void BufferedForwarderSink_PermanentFailure_DeadLettersChunk()
    {
        using var directory = new TemporaryDirectory();
        var transport = new RejectingTransport(accepted: false, reason: "permanent error");
        var options = new Buffer.Configuration.DeltaZuluBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 1,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5),
            RetryBaseDelay = TimeSpan.FromMilliseconds(10),
            RetryMaxDelay = TimeSpan.FromMilliseconds(50),
            MaxRetryAttempts = 2
        };

        using var sink = new BufferedForwarderSink(options, transport);
        sink.OnNext(CreateTestOutputRecord());
        sink.OnCompleted();

        Thread.Sleep(500);

        var health = sink.GetHealthSnapshot();
        var deadLetterDir = Path.Combine(directory.Path, "deadletter");
        var hasDeadLettered = health.BatchesDeadLetteredTotal > 0
            || (Directory.Exists(deadLetterDir) && Directory.EnumerateFiles(deadLetterDir, "*.chunk").Any());
        Assert.IsTrue(hasDeadLettered, "Expected dead-lettered chunks after retry exhaustion");
    }

    [TestMethod]
    public void BufferedForwarderSink_TransientFailure_SchedulesRetry()
    {
        using var directory = new TemporaryDirectory();
        long callCount = 0;
        var transport = new CallbackTransport(batch => {
            var attempt = Interlocked.Increment(ref callCount);
            if (attempt <= 2)
            {
                return new DeliveryAck { BatchId = batch.BatchId, Accepted = false, Reason = "transient" };
            }
            return new DeliveryAck { BatchId = batch.BatchId, Accepted = true };
        });

        var options = new Buffer.Configuration.DeltaZuluBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 1,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5),
            RetryBaseDelay = TimeSpan.FromMilliseconds(50),
            RetryMaxDelay = TimeSpan.FromMilliseconds(200),
            MaxRetryAttempts = 5
        };

        using var sink = new BufferedForwarderSink(options, transport);
        sink.OnNext(CreateTestOutputRecord());
        sink.OnCompleted();

        var health = sink.GetHealthSnapshot();
        Assert.IsGreaterThanOrEqualTo(2, Interlocked.Read(ref callCount), $"Expected at least 2 send attempts, got {callCount}");
        Assert.IsGreaterThanOrEqualTo(1, health.BatchesAcknowledgedTotal, "Expected at least one acknowledged batch");
        Assert.AreEqual(0, health.BatchesDeadLetteredTotal);
    }

    [TestMethod]
    public void DeliveryRecord_FromResourceOutput_AssignsUniqueDeliveryId()
    {
        var output = new ResourceOutputRecord {
            Metadata = new Dictionary<string, object?> {
                ["collectorId"] = "agent-01",
                ["sourceType"] = "Syslog",
                ["sourceName"] = "auth.log"
            },
            Event = new Dictionary<string, object?> { ["Message"] = "test" }
        };

        var record1 = DeliveryRecord.FromResourceOutput(output);
        var record2 = DeliveryRecord.FromResourceOutput(output);

        Assert.IsFalse(string.IsNullOrWhiteSpace(record1.DeliveryId));
        Assert.IsFalse(string.IsNullOrWhiteSpace(record2.DeliveryId));
        Assert.AreNotEqual(record1.DeliveryId, record2.DeliveryId,
            "Each enqueue must get a distinct delivery ID for at-least-once deduplication.");
    }

    [TestMethod]
    public void DeliveryRecordSerializer_RoundtripsDeliveryId()
    {
        var record = new DeliveryRecord {
            DeliveryId = "test-delivery-id-abc123",
            AgentId = "agent-01",
            SourceId = "Syslog:auth.log",
            RecordId = "42",
            CreatedAt = DateTimeOffset.UtcNow,
            Record = new ResourceOutputRecord {
                Metadata = new Dictionary<string, object?> { ["collectorId"] = "agent-01" },
                Event = new Dictionary<string, object?> { ["Message"] = "hello" }
            }
        };
        var serializer = new DeliveryRecordSerializer();

        var bytes = serializer.Serialize(record);
        var deserialized = JsonSerializer.Deserialize<DeliveryRecord>(bytes.Span, TestJson.Options);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual("test-delivery-id-abc123", deserialized.DeliveryId);
    }

    [TestMethod]
    public void ForwarderHealthReporter_EmitHealthSnapshot_WritesToDiagnosticSink()
    {
        using var directory = new TemporaryDirectory();
        var transport = new CapturingTransport();
        var options = new Buffer.Configuration.DeltaZuluBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 10,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        using var forwarderSink = new BufferedForwarderSink(options, transport);
        var capturingSink = new CapturingResourceSink();
        var metadata = new CollectorObservationMetadata { AgentId = "agent-01", HostId = "host-01" };

        using var reporter = new ForwarderHealthReporter(
            forwarderSink,
            capturingSink,
            metadata,
            interval: TimeSpan.FromHours(1));

        reporter.EmitHealthSnapshot();

        Assert.HasCount(1, capturingSink.Records);
        var record = capturingSink.Records[0];
        Assert.AreEqual(ForwarderHealthObservation.RecordKind, record.Metadata["recordKind"]);
        Assert.AreEqual("agent-01", record.Metadata["agentId"]);
        Assert.IsTrue(record.Event.ContainsKey("bufferState"));
        Assert.IsTrue(record.Event.ContainsKey("batchesSentTotal"));
    }

    [TestMethod]
    public void ForwarderHealthReporter_AfterDispose_EmitIsNoop()
    {
        using var directory = new TemporaryDirectory();
        var transport = new CapturingTransport();
        var options = new Buffer.Configuration.DeltaZuluBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 10,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        using var forwarderSink = new BufferedForwarderSink(options, transport);
        var capturingSink = new CapturingResourceSink();
        var metadata = new CollectorObservationMetadata { AgentId = "agent-01", HostId = "host-01" };

        var reporter = new ForwarderHealthReporter(
            forwarderSink,
            capturingSink,
            metadata,
            interval: TimeSpan.FromHours(1));

        reporter.Dispose();
        reporter.EmitHealthSnapshot();

        Assert.HasCount(0, capturingSink.Records);
    }

    [TestMethod]
    public void DeliveryRecord_FromResourceOutput_FallsBackToHostnameWhenCollectorIdMissing()
    {
        var output = new ResourceOutputRecord {
            Metadata = new Dictionary<string, object?> {
                ["hostname"] = "webhost-01",
                ["sourceType"] = "LinuxAuditd",
                ["sourceName"] = "auditd"
            },
            Event = new Dictionary<string, object?> {
                ["ID"] = "1710000000.123:42"
            }
        };

        var delivery = DeliveryRecord.FromResourceOutput(output);

        Assert.AreEqual("webhost-01", delivery.AgentId);
        Assert.AreEqual("LinuxAuditd:auditd", delivery.SourceId);
        Assert.AreEqual("1710000000.123:42", delivery.RecordId);
    }

    [TestMethod]
    public void DeliveryRecord_FromResourceOutput_FallsBackToMachineNameForMissingCollectorId()
    {
        var output = new ResourceOutputRecord {
            Metadata = new Dictionary<string, object?> {
                ["sourceType"] = "Syslog",
                ["sourceName"] = "auth.log"
            },
            Event = new Dictionary<string, object?> {
                ["Message"] = "test"
            }
        };

        var delivery = DeliveryRecord.FromResourceOutput(output);

        Assert.AreEqual(Environment.MachineName, delivery.AgentId);
        Assert.AreEqual("Syslog:auth.log", delivery.SourceId);
        Assert.IsNull(delivery.ProfileId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(delivery.RecordId));
    }

    [TestMethod]
    public void DeliveryRecord_FromResourceOutput_PreservesAgentSourceProfileAndRecordIdentity()
    {
        var output = new ResourceOutputRecord {
            Metadata = new Dictionary<string, object?> {
                ["collectorId"] = "agent-01",
                ["sourceType"] = "Syslog",
                ["sourceName"] = "auth.log",
                ["profileId"] = "linux.sshd"
            },
            Event = new Dictionary<string, object?> {
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
    public void DeliveryRecordSerializer_RoundtripsViaJson()
    {
        var original = CreateTestDeliveryRecord();
        var serializer = new DeliveryRecordSerializer();

        var bytes = serializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<DeliveryRecord>(bytes.Span, TestJson.Options);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.AgentId, deserialized.AgentId);
        Assert.AreEqual(original.SourceId, deserialized.SourceId);
        Assert.AreEqual(original.RecordId, deserialized.RecordId);
        Assert.AreEqual(original.ProfileId, deserialized.ProfileId);
    }

    [TestMethod]
    public async Task ForwarderChunkSender_DecodesChunkAndSendsDeliveryBatch()
    {
        using var directory = new TemporaryDirectory();
        var chunkId = ChunkId.NewChunkId();
        var record = new DeliveryRecord {
            AgentId = "agent-01",
            SourceId = "Syslog:auth.log",
            ProfileId = "linux.sshd",
            RecordId = "42",
            CreatedAt = DateTimeOffset.UtcNow,
            Record = new ResourceOutputRecord {
                Metadata = new Dictionary<string, object?> { ["collectorId"] = "agent-01" },
                Event = new Dictionary<string, object?> { ["Message"] = "hello" }
            }
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(record, TestJson.Options);
        var chunkPath = Path.Combine(directory.Path, $"{chunkId}.chunk");
        var metadataPath = Path.Combine(directory.Path, $"{chunkId}.meta.json");
        await File.WriteAllBytesAsync(chunkPath, CreateChunkBytes(payload), TestContext.CancellationToken);
        await File.WriteAllTextAsync(metadataPath, "{}", TestContext.CancellationToken);

        var transport = new CapturingTransport();
        var sender = new ForwarderChunkSender(transport);
        var result = await sender.SendAsync(new StoredChunk {
            Id = chunkId,
            ChunkFilePath = chunkPath,
            MetadataFilePath = metadataPath,
            Metadata = new ChunkMetadata {
                ChunkId = chunkId.Value,
                CreatedUtc = DateTimeOffset.UtcNow,
                SealedUtc = DateTimeOffset.UtcNow,
                RecordCount = 1,
                PayloadBytes = payload.Length,
                Checksum = "sha256:test"
            }
        }, TestContext.CancellationToken);

        Assert.AreEqual(ChunkSendStatus.Success, result.Status);
        Assert.IsNotNull(transport.Batch);
        Assert.AreEqual(chunkId.Value, transport.Batch!.BatchId);
        Assert.HasCount(1, transport.Batch.Records);
        Assert.AreEqual("42", transport.Batch.Records[0].RecordId);
    }

    [TestMethod]
    public async Task ForwarderChunkSender_RecordCountMismatch_ReturnsPermanentFailure()
    {
        using var directory = new TemporaryDirectory();
        var chunkId = ChunkId.NewChunkId();
        var record = CreateTestDeliveryRecord();

        var payload = JsonSerializer.SerializeToUtf8Bytes(record, TestJson.Options);
        var chunkPath = Path.Combine(directory.Path, $"{chunkId}.chunk");
        var metadataPath = Path.Combine(directory.Path, $"{chunkId}.meta.json");
        await File.WriteAllBytesAsync(chunkPath, CreateChunkBytes(payload), TestContext.CancellationToken);
        await File.WriteAllTextAsync(metadataPath, "{}", TestContext.CancellationToken);

        var transport = new CapturingTransport();
        var sender = new ForwarderChunkSender(transport);
        var result = await sender.SendAsync(new StoredChunk {
            Id = chunkId,
            ChunkFilePath = chunkPath,
            MetadataFilePath = metadataPath,
            Metadata = CreateChunkMetadata(chunkId, payload.Length, recordCount: 99)
        }, TestContext.CancellationToken);

        Assert.AreEqual(ChunkSendStatus.PermanentFailure, result.Status);
        Assert.IsNull(transport.Batch);
        Assert.Contains("expected 99 records but decoded 1", result.Error!);
    }

    [TestMethod]
    public async Task ForwarderChunkSender_TransientFailure_ReturnsTransientStatus()
    {
        using var directory = new TemporaryDirectory();
        var chunkId = ChunkId.NewChunkId();
        var record = CreateTestDeliveryRecord();

        var payload = JsonSerializer.SerializeToUtf8Bytes(record, TestJson.Options);
        var chunkPath = Path.Combine(directory.Path, $"{chunkId}.chunk");
        var metadataPath = Path.Combine(directory.Path, $"{chunkId}.meta.json");
        await File.WriteAllBytesAsync(chunkPath, CreateChunkBytes(payload), TestContext.CancellationToken);
        await File.WriteAllTextAsync(metadataPath, "{}", TestContext.CancellationToken);

        var transport = new RejectingTransport(accepted: false, reason: "server busy");
        var sender = new ForwarderChunkSender(transport);
        var result = await sender.SendAsync(new StoredChunk {
            Id = chunkId,
            ChunkFilePath = chunkPath,
            MetadataFilePath = metadataPath,
            Metadata = CreateChunkMetadata(chunkId, payload.Length, recordCount: 1)
        }, TestContext.CancellationToken);

        Assert.AreEqual(ChunkSendStatus.TransientFailure, result.Status);
        Assert.Contains("server busy", result.Error!);
    }

    [TestMethod]
    public void ForwarderHealthObservation_ToOutputRecord_ContainsAllExpectedFields()
    {
        var observation = new ForwarderHealthObservation {
            Metadata = new CollectorObservationMetadata { AgentId = "agent-01", HostId = "host-01" },
            Health = new ForwarderHealthSnapshot {
                Buffer = new BufferSnapshot {
                    State = BufferState.Healthy,
                    DiskBytesUsed = 1024,
                    DiskBytesLimit = 1024 * 1024,
                    MemoryBytesUsed = 512,
                    OpenChunkBytes = 256,
                    SealedChunkCount = 2,
                    RetryQueueDepth = 0,
                    OldestChunkAge = TimeSpan.FromSeconds(5),
                    RecordsAcceptedTotal = 100,
                    RecordsRejectedTotal = 0,
                    RecordsDroppedTotal = 0,
                    ChunksDeliveredTotal = 10,
                    ChunksDeadLetteredTotal = 0
                },
                BatchesSentTotal = 10,
                BatchesAcknowledgedTotal = 9,
                BatchesFailedTotal = 1,
                BatchesRetryScheduledTotal = 2,
                BatchesDeadLetteredTotal = 0,
                LastForwarderActivityUtc = DateTimeOffset.UtcNow
            }
        };

        var record = observation.ToOutputRecord();

        Assert.AreEqual(ForwarderHealthObservation.RecordKind, record.Metadata["recordKind"]);
        Assert.AreEqual("Healthy", record.Event["bufferState"]);
        Assert.AreEqual(100L, record.Event["recordsAcceptedTotal"]);
        Assert.AreEqual(10L, record.Event["chunksDeliveredTotal"]);
        Assert.AreEqual(10L, record.Event["batchesSentTotal"]);
        Assert.AreEqual(9L, record.Event["batchesAcknowledgedTotal"]);
        Assert.AreEqual(1L, record.Event["batchesFailedTotal"]);
        Assert.AreEqual(2L, record.Event["batchesRetryScheduledTotal"]);
        Assert.AreEqual(0L, record.Event["batchesDeadLetteredTotal"]);
        Assert.IsNotNull(record.Event["lastForwarderActivityUtc"]);
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

    private static ChunkMetadata CreateChunkMetadata(ChunkId chunkId, int payloadLength, int recordCount) => new() {
        ChunkId = chunkId.Value,
        CreatedUtc = DateTimeOffset.UtcNow,
        SealedUtc = DateTimeOffset.UtcNow,
        RecordCount = recordCount,
        PayloadBytes = payloadLength,
        Checksum = "sha256:test"
    };

    private static DeliveryRecord CreateTestDeliveryRecord() => new() {
        AgentId = "agent-01",
        SourceId = "Syslog:auth.log",
        ProfileId = "linux.sshd",
        RecordId = "42",
        CreatedAt = DateTimeOffset.UtcNow,
        Record = CreateTestOutputRecord()
    };

    private static ResourceOutputRecord CreateTestOutputRecord() => new() {
        Metadata = new Dictionary<string, object?> {
            ["collectorId"] = "agent-01",
            ["sourceType"] = "Syslog",
            ["sourceName"] = "auth.log",
            ["profileId"] = "linux.sshd"
        },
        Event = new Dictionary<string, object?> {
            ["RecordId"] = "42",
            ["Message"] = "accepted publickey"
        }
    };

    private static class TestJson
    {
        public static JsonSerializerOptions Options { get; } = Outputs.Ndjson.NdjsonSerializerOptions.CreateDefault();
    }

    private sealed class CallbackTransport : IForwarderTransport
    {
        private readonly Func<DeliveryBatch, DeliveryAck> _handler;

        public CallbackTransport(Func<DeliveryBatch, DeliveryAck> handler)
        {
            _handler = handler;
        }

        public ValueTask<DeliveryAck> SendAsync(DeliveryBatch batch, CancellationToken cancellationToken = default) => ValueTask.FromResult(_handler(batch));
    }

    private sealed class CapturingTransport : IForwarderTransport
    {
        public DeliveryBatch? Batch { get; private set; }

        public ValueTask<DeliveryAck> SendAsync(DeliveryBatch batch, CancellationToken cancellationToken = default)
        {
            Batch = batch;
            return ValueTask.FromResult(new DeliveryAck {
                BatchId = batch.BatchId,
                Accepted = true
            });
        }
    }

    private sealed class RejectingTransport : IForwarderTransport
    {
        private readonly bool _accepted;
        private readonly string? _reason;

        public RejectingTransport(bool accepted, string? reason = null)
        {
            _accepted = accepted;
            _reason = reason;
        }

        public ValueTask<DeliveryAck> SendAsync(DeliveryBatch batch, CancellationToken cancellationToken = default) => ValueTask.FromResult(new DeliveryAck {
            BatchId = batch.BatchId,
            Accepted = _accepted,
            Reason = _reason
        });
    }

    private sealed class CapturingResourceSink : IResourceSink
    {
        public string Name => "capturing";
        public List<ResourceOutputRecord> Records { get; } = [];
        public void OnNext(ResourceOutputRecord value) => Records.Add(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
        public void Dispose() { }
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

    public TestContext TestContext { get; set; }
}