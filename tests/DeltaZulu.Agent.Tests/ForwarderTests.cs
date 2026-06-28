using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Delivery;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Observability;
using DeltaZulu.Pipeline.Inputs.Relp;
using DeltaZulu.Pipeline.Outputs.Relp;
using DeltaZulu.Pipeline.Core.Ndjson;
using DeltaZulu.Pipeline.Core.Relp;
using DeltaZulu.Pipeline.Core.MessagePack;
using DeltaZulu.DurableBuffer.Abstractions;
using DeltaZulu.DurableBuffer.Chunks;
using DeltaZulu.DurableBuffer.Dispatch;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ForwarderTests
{
    [TestMethod]
    public void BufferedRelpSink_HealthSnapshot_ReportsBufferAndForwarderCounters()
    {
        using var directory = new TemporaryDirectory();
        var transport = new CapturingTransport();
        var options = new DurableBuffer.Configuration.DurableBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 1,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        using var sink = new BufferedRelpSink(options, transport);
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
        Assert.IsGreaterThanOrEqualTo(1, health.Buffer.ChunksSentTotal);
        Assert.IsGreaterThanOrEqualTo(1, health.Buffer.ChunksDeliveredTotal);
        Assert.AreEqual(0, health.Buffer.ChunksDeadLetteredTotal);
        Assert.IsNotNull(health.LastForwarderActivityUtc);

        var observation = sink.GetHealthOutputRecord(new CollectorObservationMetadata {
            AgentId = "agent-01",
            HostId = "host-01"
        });

        Assert.AreEqual(RelpHealthObservation.RecordKind, observation.Metadata["recordKind"]);
        Assert.AreEqual(1L, observation.Event["recordsAcceptedTotal"]);
        Assert.AreEqual(0L, observation.Event["chunksDeadLetteredTotal"]);
        Assert.IsTrue(observation.Event.ContainsKey("chunksSentTotal"));
    }

    [TestMethod]
    public void BufferedRelpSink_PermanentFailure_DeadLettersChunk()
    {
        using var directory = new TemporaryDirectory();
        var transport = new RejectingTransport(accepted: false, reason: "permanent error");
        var options = new DurableBuffer.Configuration.DurableBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 1,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5),
            RetryBaseDelay = TimeSpan.FromMilliseconds(10),
            RetryMaxDelay = TimeSpan.FromMilliseconds(50),
            MaxRetryAttempts = 2
        };

        using var sink = new BufferedRelpSink(options, transport);
        sink.OnNext(CreateTestOutputRecord());
        sink.OnCompleted();

        Thread.Sleep(500);

        var health = sink.GetHealthSnapshot();
        var deadLetterDir = Path.Combine(directory.Path, "deadletter");
        var hasDeadLettered = health.Buffer.ChunksDeadLetteredTotal > 0
            || (Directory.Exists(deadLetterDir) && Directory.EnumerateFiles(deadLetterDir, "*.chunk").Any());
        Assert.IsTrue(hasDeadLettered, "Expected dead-lettered chunks after retry exhaustion");
    }

    [TestMethod]
    public void BufferedRelpSink_TransientFailure_SchedulesRetry()
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

        var options = new DurableBuffer.Configuration.DurableBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 1,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5),
            RetryBaseDelay = TimeSpan.FromMilliseconds(50),
            RetryMaxDelay = TimeSpan.FromMilliseconds(200),
            MaxRetryAttempts = 5
        };

        using var sink = new BufferedRelpSink(options, transport);
        sink.OnNext(CreateTestOutputRecord());
        sink.OnCompleted();

        var health = sink.GetHealthSnapshot();
        Assert.IsGreaterThanOrEqualTo(2, Interlocked.Read(ref callCount), $"Expected at least 2 send attempts, got {callCount}");
        Assert.IsGreaterThanOrEqualTo(1, health.Buffer.ChunksDeliveredTotal, "Expected at least one acknowledged batch");
        Assert.AreEqual(0, health.Buffer.ChunksDeadLetteredTotal);
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
    public void RelpDeliveryRecordSerializer_RoundtripsDeliveryId()
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
        var serializer = new RelpDeliveryRecordSerializer();

        var bytes = serializer.Serialize(record);
        var deserialized = JsonSerializer.Deserialize<DeliveryRecord>(bytes.Span, TestJson.Options);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual("test-delivery-id-abc123", deserialized.DeliveryId);
    }

    [TestMethod]
    public void RelpHealthReporter_EmitHealthSnapshot_WritesToDiagnosticSink()
    {
        using var directory = new TemporaryDirectory();
        var transport = new CapturingTransport();
        var options = new DurableBuffer.Configuration.DurableBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 10,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        using var relpSink = new BufferedRelpSink(options, transport);
        var capturingSink = new CapturingResourceSink();
        var metadata = new CollectorObservationMetadata { AgentId = "agent-01", HostId = "host-01" };

        using var reporter = new RelpHealthReporter(
            relpSink,
            capturingSink,
            metadata,
            interval: TimeSpan.FromHours(1));

        reporter.EmitHealthSnapshot();

        Assert.HasCount(1, capturingSink.Records);
        var record = capturingSink.Records[0];
        Assert.AreEqual(RelpHealthObservation.RecordKind, record.Metadata["recordKind"]);
        Assert.AreEqual("agent-01", record.Metadata["agentId"]);
        Assert.IsTrue(record.Event.ContainsKey("bufferState"));
        Assert.IsTrue(record.Event.ContainsKey("chunksSentTotal"));
    }

    [TestMethod]
    public void RelpHealthReporter_AfterDispose_EmitIsNoop()
    {
        using var directory = new TemporaryDirectory();
        var transport = new CapturingTransport();
        var options = new DurableBuffer.Configuration.DurableBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 10,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        using var relpSink = new BufferedRelpSink(options, transport);
        var capturingSink = new CapturingResourceSink();
        var metadata = new CollectorObservationMetadata { AgentId = "agent-01", HostId = "host-01" };

        var reporter = new RelpHealthReporter(
            relpSink,
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
    public void RelpDeliveryRecordSerializer_RoundtripsViaJson()
    {
        var original = CreateTestDeliveryRecord();
        var serializer = new RelpDeliveryRecordSerializer();

        var bytes = serializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<DeliveryRecord>(bytes.Span, TestJson.Options);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.AgentId, deserialized.AgentId);
        Assert.AreEqual(original.SourceId, deserialized.SourceId);
        Assert.AreEqual(original.RecordId, deserialized.RecordId);
        Assert.AreEqual(original.ProfileId, deserialized.ProfileId);
    }

    [TestMethod]
    public async Task RelpChunkSender_DecodesChunkAndSendsDeliveryBatch()
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
        var sender = new RelpChunkSender(transport);
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
    public async Task RelpChunkSender_RecordCountMismatch_ReturnsPermanentFailure()
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
        var sender = new RelpChunkSender(transport);
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
    public async Task RelpChunkSender_TransientFailure_ReturnsTransientStatus()
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
        var sender = new RelpChunkSender(transport);
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
    public void YamlRelpOutputConfigurationLoader_LoadFile_LoadsBufferAndRelpSettings()
    {
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "forwarder.yaml");
        File.WriteAllText(configPath, """
            id: test-forwarder
            buffer:
              path: /tmp/dz-buffer
              maxChunkRecords: 25
              maxChunkAgeSeconds: 3.5
            relp:
              useTls: true
              tls:
                certificateValidation: Thumbprint
                allowedServerCertificateThumbprints:
                  - 00112233445566778899AABBCCDDEEFF00112233
                certificateExpiryWarningDays: 14
                clientCertificateEnabled: false
                clientCertificatePath: /tmp/missing-dev-client-cert.pfx
              endpoints:
                - host: relp-a.example
                  port: 6514
                - host: relp-b.example
                  port: 6515
            """);

        var configuration = new YamlRelpOutputConfigurationLoader().LoadFile(configPath);

        Assert.AreEqual("test-forwarder", configuration.Id);
        Assert.AreEqual("/tmp/dz-buffer", configuration.Buffer.Path);
        Assert.AreEqual(25, configuration.Buffer.MaxChunkRecords);
        Assert.AreEqual(3.5, configuration.Buffer.MaxChunkAgeSeconds);
        Assert.IsTrue(configuration.Relp.UseTls);
        Assert.AreEqual(RelpCertificateValidationMode.Thumbprint, configuration.Relp.Tls.CertificateValidation);
        Assert.HasCount(1, configuration.Relp.Tls.AllowedServerCertificateThumbprints);
        Assert.AreEqual(14, configuration.Relp.Tls.CertificateExpiryWarningDays);
        Assert.IsFalse(configuration.Relp.Tls.ClientCertificateEnabled);
        Assert.AreEqual("/tmp/missing-dev-client-cert.pfx", configuration.Relp.Tls.ClientCertificatePath);
        Assert.HasCount(2, configuration.Relp.Endpoints);
        Assert.AreEqual("relp-b.example", configuration.Relp.Endpoints[1].Host);
        Assert.AreEqual(6515, configuration.Relp.Endpoints[1].Port);
    }

    [TestMethod]
    public void RelpInput_Constructor_RejectsTlsInputWithoutCertificate() =>
        Assert.ThrowsExactly<InvalidDataException>(() => new RelpInput(new RelpInputConfiguration {
            Address = "127.0.0.1",
            Port = 6514,
            UseTls = true
        }));

    [TestMethod]
    public void YamlRelpOutputConfigurationLoader_LoadFile_RejectsInvalidEndpoint()
    {
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "forwarder.yaml");
        File.WriteAllText(configPath, """
            buffer:
              path: /tmp/dz-buffer
            relp:
              endpoints:
                - host: ""
                  port: 6514
            """);

        Assert.ThrowsExactly<InvalidDataException>(() => new YamlRelpOutputConfigurationLoader().LoadFile(configPath));
    }


    [TestMethod]
    public void YamlRelpOutputConfigurationLoader_LoadFile_RejectsThumbprintValidationWithoutThumbprints()
    {
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "forwarder.yaml");
        File.WriteAllText(configPath, """
            relp:
              useTls: true
              tls:
                certificateValidation: Thumbprint
              endpoints:
                - host: relp.example
                  port: 6514
            """);

        Assert.ThrowsExactly<InvalidDataException>(() => new YamlRelpOutputConfigurationLoader().LoadFile(configPath));
    }

    [TestMethod]
    public void RelpForwarderOptions_GetConfiguredEndpoints_UsesFailoverListWhenProvided()
    {
        var options = new RelpForwarderOptions {
            Host = "primary.example",
            Port = 6514,
            Endpoints = [
                new RelpEndpoint { Host = "relp-a.example", Port = 6514 },
                new RelpEndpoint { Host = "relp-b.example", Port = 6515 }
            ]
        };

        var endpoints = options.GetConfiguredEndpoints();

        Assert.HasCount(2, endpoints);
        Assert.AreEqual("relp-a.example", endpoints[0].Host);
        Assert.AreEqual(6515, endpoints[1].Port);
    }

    [TestMethod]
    public void RelpForwarderTransport_Constructor_RejectsInvalidFailoverEndpoint() => Assert.ThrowsExactly<ArgumentException>(() => new RelpForwarderTransport(new RelpForwarderOptions {
        Host = "primary.example",
        Port = 6514,
        Endpoints = [new RelpEndpoint { Host = " ", Port = 6514 }]
    }));

    [TestMethod]
    public async Task RelpForwarderTransport_SendAsync_SendsBatchAndReturnsAcceptedAck()
    {
        using var timeout = CreateRelpTestTimeout();
        await using var server = await FakeRelpServer.StartAsync(acceptSyslog: true, timeout.Token);
        await using var transport = new RelpForwarderTransport(new RelpForwarderOptions
        {
            Host = "127.0.0.1",
            Port = server.Port
        });

        var batch = new DeliveryBatch
        {
            BatchId = "batch-01",
            Records = [CreateTestDeliveryRecord()]
        };

        var ack = await transport.SendAsync(batch, timeout.Token).AsTask().WaitAsync(timeout.Token);

        Assert.IsTrue(ack.Accepted);
        Assert.AreEqual("batch-01", ack.BatchId);

        var receivedBatch = await server.GetSyslogBatchAsync(timeout.Token);
        Assert.IsNotNull(receivedBatch);
        Assert.AreEqual("batch-01", receivedBatch.BatchId);
        Assert.HasCount(1, receivedBatch.Records);
        Assert.AreEqual("42", receivedBatch.Records[0].RecordId);
    }

    [TestMethod]
    public async Task RelpForwarderTransport_SendAsync_ReturnsRejectedAckWhenRelpServerRejectsBatch()
    {
        using var timeout = CreateRelpTestTimeout();
        await using var server = await FakeRelpServer.StartAsync(acceptSyslog: false, timeout.Token);
        await using var transport = new RelpForwarderTransport(new RelpForwarderOptions
        {
            Host = "127.0.0.1",
            Port = server.Port
        });

        var ack = await transport.SendAsync(new DeliveryBatch
        {
            BatchId = "batch-02",
            Records = [CreateTestDeliveryRecord()]
        }, timeout.Token).AsTask().WaitAsync(timeout.Token);

        Assert.IsFalse(ack.Accepted);
        Assert.AreEqual("batch-02", ack.BatchId);
        Assert.Contains("RELP send failed", ack.Reason!);
    }

    [TestMethod]
    public async Task RelpForwarderTransport_Dispose_IsIdempotentAcrossSyncAndAsyncCalls()
    {
        var transport = new RelpForwarderTransport(new RelpForwarderOptions
        {
            Host = "127.0.0.1",
            Port = 6514
        });

        transport.Dispose();
        transport.Dispose();
        await transport.DisposeAsync();
    }

    [TestMethod]
    public void RelpHealthObservation_ToOutputRecord_ContainsAllExpectedFields()
    {
        var now = DateTimeOffset.UtcNow;
        var observation = new RelpHealthObservation {
            Metadata = new CollectorObservationMetadata { AgentId = "agent-01", HostId = "host-01" },
            Health = new RelpHealthSnapshot {
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
                    ChunksSentTotal = 10,
                    ChunksDeliveredTotal = 9,
                    ChunksFailedTotal = 1,
                    ChunksRetryScheduledTotal = 2,
                    ChunksDeadLetteredTotal = 0
                },
                LastForwarderActivityUtc = now
            }
        };

        var record = observation.ToOutputRecord();

        Assert.AreEqual(RelpHealthObservation.RecordKind, record.Metadata["recordKind"]);
        Assert.AreEqual("Healthy", record.Event["bufferState"]);
        Assert.AreEqual(100L, record.Event["recordsAcceptedTotal"]);
        Assert.AreEqual(9L, record.Event["chunksDeliveredTotal"]);
        Assert.AreEqual(10L, record.Event["chunksSentTotal"]);
        Assert.AreEqual(9L, record.Event["chunksDeliveredTotal"]);
        Assert.AreEqual(1L, record.Event["chunksFailedTotal"]);
        Assert.AreEqual(2L, record.Event["chunksRetryScheduledTotal"]);
        Assert.AreEqual(0L, record.Event["chunksDeadLetteredTotal"]);
        Assert.AreEqual(now, record.Event["lastForwarderActivityUtc"]);
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
        public static JsonSerializerOptions Options { get; } = NdjsonSerializerOptions.CreateDefault();
    }

    private sealed class CallbackTransport : IDeliveryTransport
    {
        private readonly Func<DeliveryBatch, DeliveryAck> _handler;

        public CallbackTransport(Func<DeliveryBatch, DeliveryAck> handler)
        {
            _handler = handler;
        }

        public ValueTask<DeliveryAck> SendAsync(DeliveryBatch batch, CancellationToken cancellationToken = default) => ValueTask.FromResult(_handler(batch));
    }

    private sealed class CapturingTransport : IDeliveryTransport
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

    private sealed class RejectingTransport : IDeliveryTransport
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

    private CancellationTokenSource CreateRelpTestTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        return timeout;
    }

    private sealed class FakeRelpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly bool _acceptSyslog;
        private readonly Task _serverTask;
        private readonly TaskCompletionSource<DeliveryBatch?> _batchSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _cts = new();
        private TcpClient? _client;

        private FakeRelpServer(TcpListener listener, bool acceptSyslog)
        {
            _listener = listener;
            _acceptSyslog = acceptSyslog;
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverTask = Task.Run(RunAsync);
        }

        public int Port { get; }

        public static Task<FakeRelpServer> StartAsync(bool acceptSyslog, CancellationToken cancellationToken)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new FakeRelpServer(listener, acceptSyslog));
        }

        public async Task<DeliveryBatch?> GetSyslogBatchAsync(CancellationToken cancellationToken)
        {
            await using var registration = cancellationToken.UnsafeRegister(static state =>
                ((TaskCompletionSource<DeliveryBatch?>)state!).TrySetCanceled(), _batchSource);
            return await _batchSource.Task.ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();
            _client?.Close();
            _batchSource.TrySetCanceled(_cts.Token);
            try
            {
                await _serverTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch
            {
            }
            _client?.Dispose();
            _cts.Dispose();
        }

        private async Task RunAsync()
        {
            try
            {
                _client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                await using var stream = _client.GetStream();

                while (!_cts.IsCancellationRequested)
                {
                    var maybeFrame = await RelpFrameCodec.ReadFrameAsync(stream, _cts.Token).ConfigureAwait(false);
                    if (maybeFrame is not { } frame)
                    {
                        return;
                    }

                    switch (frame.Command)
                    {
                        case "open":
                            await RelpFrameCodec.WriteResponseAsync(stream, frame.TransactionId, "200 OK\nrelp_version=0\ncommands=syslog", _cts.Token).ConfigureAwait(false);
                            break;
                        case "syslog":
                            _batchSource.TrySetResult(new MessagePackPayloadSerializer().Deserialize<DeliveryBatch>(frame.Payload));
                            await RelpFrameCodec.WriteResponseAsync(stream, frame.TransactionId, _acceptSyslog ? "200 OK" : "500 rejected", _cts.Token).ConfigureAwait(false);
                            if (!_acceptSyslog)
                            {
                                return;
                            }
                            break;
                        case "close":
                            await RelpFrameCodec.WriteResponseAsync(stream, frame.TransactionId, "200 OK", _cts.Token).ConfigureAwait(false);
                            return;
                    }
                }
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_cts.IsCancellationRequested)
            {
            }
            finally
            {
                _batchSource.TrySetCanceled(_cts.Token);
            }
        }

    }

    private sealed class CapturingResourceSink : IOutputWriter
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
