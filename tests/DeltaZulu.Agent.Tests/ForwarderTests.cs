using System.Net;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Text.Json;
using DeltaZulu.DurableBuffer.Abstractions;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Delivery;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Forwarder;
using DeltaZulu.Pipeline.Core.MessagePack;
using DeltaZulu.Pipeline.Core.Ndjson;
using DeltaZulu.Pipeline.Core.Observability;
using DeltaZulu.Pipeline.Inputs.Forwarder;
using DeltaZulu.Pipeline.Outputs.Forwarder;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ForwarderTests
{
    [TestMethod]
    public void BufferedForwarderSink_HealthSnapshot_ReportsBufferAndForwarderCounters()
    {
        using var directory = new TemporaryDirectory();
        var transport = new CapturingTransport();
        var options = new DurableBuffer.Configuration.DurableBufferOptions {
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
        Assert.IsGreaterThanOrEqualTo(1, health.Buffer.ChunksCompletedTotal);
        Assert.AreEqual(0, health.Buffer.ChunksDeadLetteredTotal);
        Assert.IsNotNull(health.Transport);
        Assert.IsGreaterThanOrEqualTo(1, health.Transport!.SendAttemptsTotal);
        Assert.IsGreaterThanOrEqualTo(1, health.Transport.SendSuccessesTotal);
        Assert.IsNotNull(health.LastForwarderActivityUtc);

        var observation = sink.GetHealthOutputRecord(new CollectorObservationMetadata {
            AgentId = "agent-01",
            HostId = "host-01"
        });

        Assert.AreEqual(ForwarderHealthObservation.RecordKind, observation.Metadata["recordKind"]);
        Assert.AreEqual(1L, observation.Event["recordsAcceptedTotal"]);
        Assert.AreEqual(0L, observation.Event["chunksDeadLetteredTotal"]);
        Assert.IsTrue(observation.Event.ContainsKey("chunksCompletedTotal"));
        Assert.IsTrue(observation.Event.ContainsKey("transportSendAttemptsTotal"));
    }

    [TestMethod]
    public void BufferedForwarderSink_OnNextAfterDispose_IsNoop()
    {
        using var directory = new TemporaryDirectory();
        var transport = new CapturingTransport();
        var options = new DurableBuffer.Configuration.DurableBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 1,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        var sink = new BufferedForwarderSink(options, transport);
        sink.Dispose();

        sink.OnNext(CreateTestOutputRecord());
    }

    [TestMethod]
    public void BufferedForwarderSink_PermanentFailure_DeadLettersChunk()
    {
        using var directory = new TemporaryDirectory();
        var transport = new RejectingTransport(accepted: false, reason: "permanent error");
        var options = new DurableBuffer.Configuration.DurableBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 1,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };
        var retry = new ForwarderRetryConfiguration {
            MaxAttempts = 2,
            BaseDelay = TimeSpan.FromMilliseconds(10),
            MaxDelay = TimeSpan.FromMilliseconds(50)
        };

        using var sink = new BufferedForwarderSink(options, transport, retry);
        sink.OnNext(CreateTestOutputRecord());
        SpinWait.SpinUntil(
            () => sink.GetHealthSnapshot().Transport?.ChunksDeadLetteredTotal > 0
                || sink.GetHealthSnapshot().Buffer.ChunksDeadLetteredTotal > 0,
            TimeSpan.FromSeconds(5));
        sink.OnCompleted();

        var health = sink.GetHealthSnapshot();
        var deadLetterDir = Path.Combine(directory.Path, "deadletter");
        var hasDeadLettered = health.Buffer.ChunksDeadLetteredTotal > 0
            || health.Transport?.ChunksDeadLetteredTotal > 0
            || (Directory.Exists(deadLetterDir) && Directory.EnumerateFiles(deadLetterDir).Any());
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

        var options = new DurableBuffer.Configuration.DurableBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 1,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };
        var retry = new ForwarderRetryConfiguration {
            MaxAttempts = 5,
            BaseDelay = TimeSpan.FromMilliseconds(50),
            MaxDelay = TimeSpan.FromMilliseconds(200)
        };

        using var sink = new BufferedForwarderSink(options, transport, retry);
        sink.OnNext(CreateTestOutputRecord());
        SpinWait.SpinUntil(() => Interlocked.Read(ref callCount) >= 3, TimeSpan.FromSeconds(5));
        sink.OnCompleted();

        var health = sink.GetHealthSnapshot();
        Assert.IsGreaterThanOrEqualTo(2, Interlocked.Read(ref callCount), $"Expected at least 2 send attempts, got {callCount}");
        Assert.IsGreaterThanOrEqualTo(1, health.Buffer.ChunksCompletedTotal, "Expected at least one acknowledged batch");
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
    public void ForwarderDeliveryRecordSerializer_RoundtripsDeliveryId()
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
        var serializer = new ForwarderDeliveryRecordSerializer();

        var bytes = serializer.Serialize(record);
        var deserialized = JsonSerializer.Deserialize<DeliveryRecord>(bytes.Span, TestJson.Options);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual("test-delivery-id-abc123", deserialized.DeliveryId);
    }

    [TestMethod]
    public void ForwarderInput_ToSourceEvent_AddsNestedForwarderMetadata()
    {
        var createdAt = DateTimeOffset.Parse("2026-07-02T07:21:49.4311395+00:00");
        var deliveryRecord = new DeliveryRecord {
            DeliveryId = "351ddc6968b94697b9369fe81bc9c14f",
            AgentId = "agent-01",
            SourceId = "Syslog:auth.log",
            ProfileId = "linux.sshd",
            RecordId = "05f821f93196441ab90bcd64395a836e",
            CreatedAt = createdAt,
            Record = new ResourceOutputRecord {
                Metadata = new Dictionary<string, object?> { ["collectorId"] = "agent-01" },
                Event = new Dictionary<string, object?> { ["Message"] = "hello" }
            }
        };
        var forwarderInput = new ForwarderInput(new ForwarderInputConfiguration());

        var method = typeof(ForwarderInput).GetMethod("ToSourceEvent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        var sourceEvent = Assert.IsInstanceOfType<SourceEvent>(method.Invoke(forwarderInput, [deliveryRecord]));
        var metadata = sourceEvent.Metadata.ToDictionary();

        Assert.IsFalse(metadata.ContainsKey("forwarder.deliveryId"));
        Assert.IsFalse(metadata.ContainsKey("forwarder.recordId"));
        Assert.IsFalse(metadata.ContainsKey("forwarder.createdAt"));

        var forwarder = Assert.IsInstanceOfType<Dictionary<string, object?>>(metadata["forwarder"]);
        Assert.AreEqual("351ddc6968b94697b9369fe81bc9c14f", forwarder["deliveryID"]);
        Assert.AreEqual("05f821f93196441ab90bcd64395a836e", forwarder["recordId"]);
        Assert.AreEqual(createdAt, forwarder["createdAt"]);
    }

    [TestMethod]
    public void ForwarderHealthReporter_EmitHealthSnapshot_WritesToDiagnosticSink()
    {
        using var directory = new TemporaryDirectory();
        var transport = new CapturingTransport();
        var options = new DurableBuffer.Configuration.DurableBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 10,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        using var sink = new BufferedForwarderSink(options, transport);
        var capturingSink = new CapturingResourceSink();
        var metadata = new CollectorObservationMetadata { AgentId = "agent-01", HostId = "host-01" };

        using var reporter = new ForwarderHealthReporter(
            sink,
            capturingSink,
            metadata,
            interval: TimeSpan.FromHours(1));

        reporter.EmitHealthSnapshot();

        Assert.HasCount(1, capturingSink.Records);
        var record = capturingSink.Records[0];
        Assert.AreEqual(ForwarderHealthObservation.RecordKind, record.Metadata["recordKind"]);
        Assert.AreEqual("agent-01", record.Metadata["agentId"]);
        Assert.IsTrue(record.Event.ContainsKey("bufferState"));
        Assert.IsTrue(record.Event.ContainsKey("chunksCompletedTotal"));
    }

    [TestMethod]
    public void ForwarderHealthReporter_AfterDispose_EmitIsNoop()
    {
        using var directory = new TemporaryDirectory();
        var transport = new CapturingTransport();
        var options = new DurableBuffer.Configuration.DurableBufferOptions {
            StoragePath = directory.Path,
            MaxChunkRecords = 10,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        using var sink = new BufferedForwarderSink(options, transport);
        var capturingSink = new CapturingResourceSink();
        var metadata = new CollectorObservationMetadata { AgentId = "agent-01", HostId = "host-01" };

        var reporter = new ForwarderHealthReporter(
            sink,
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
    public void ForwarderDeliveryRecordSerializer_RoundtripsViaJson()
    {
        var original = CreateTestDeliveryRecord();
        var serializer = new ForwarderDeliveryRecordSerializer();

        var bytes = serializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<DeliveryRecord>(bytes.Span, TestJson.Options);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.AgentId, deserialized.AgentId);
        Assert.AreEqual(original.SourceId, deserialized.SourceId);
        Assert.AreEqual(original.RecordId, deserialized.RecordId);
        Assert.AreEqual(original.ProfileId, deserialized.ProfileId);
    }

    [TestMethod]
    public void YamlForwarderOutputConfigurationLoader_LoadFile_LoadsBufferAndForwarderSettings()
    {
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "forwarder.yaml");
        File.WriteAllText(configPath, """
            id: test-forwarder
            buffer:
              path: /tmp/dz-buffer
              maxChunkRecords: 25
              maxChunkAgeSeconds: 3.5
            forwarder:
              useTls: true
              tls:
                certificateValidation: Thumbprint
                allowedServerCertificateThumbprints:
                  - 00112233445566778899AABBCCDDEEFF00112233
                certificateExpiryWarningDays: 14
                clientCertificateEnabled: false
                clientCertificatePath: /tmp/missing-dev-client-cert.pfx
              endpoints:
                - host: forwarder-a.example
                  port: 6514
                - host: forwarder-b.example
                  port: 6515
            """);

        var configuration = new YamlForwarderOutputConfigurationLoader().LoadFile(configPath);

        Assert.AreEqual("test-forwarder", configuration.Id);
        Assert.AreEqual("/tmp/dz-buffer", configuration.Buffer.Path);
        Assert.AreEqual(25, configuration.Buffer.MaxChunkRecords);
        Assert.AreEqual(3.5, configuration.Buffer.MaxChunkAgeSeconds);
        Assert.IsTrue(configuration.Transport.UseTls);
        Assert.AreEqual(CertificateValidationMode.Thumbprint, configuration.Transport.Tls.CertificateValidation);
        Assert.HasCount(1, configuration.Transport.Tls.AllowedServerCertificateThumbprints);
        Assert.AreEqual(14, configuration.Transport.Tls.CertificateExpiryWarningDays);
        Assert.IsFalse(configuration.Transport.Tls.ClientCertificateEnabled);
        Assert.AreEqual("/tmp/missing-dev-client-cert.pfx", configuration.Transport.Tls.ClientCertificatePath);
        Assert.HasCount(2, configuration.Transport.Endpoints);
        Assert.AreEqual("forwarder-b.example", configuration.Transport.Endpoints[1].Host);
        Assert.AreEqual(6515, configuration.Transport.Endpoints[1].Port);
    }

    [TestMethod]
    public void ForwarderInput_Constructor_RejectsTlsInputWithoutCertificate() =>
        Assert.ThrowsExactly<InvalidDataException>(() => new ForwarderInput(new ForwarderInputConfiguration {
            Address = "127.0.0.1",
            Port = 6514,
            UseTls = true
        }));

    [TestMethod]
    public void ForwarderInput_Open_WhenBindFails_ReportsEndpointContext()
    {
        var occupiedListener = new TcpListener(IPAddress.Loopback, 0);
        occupiedListener.Start();
        try
        {
            var port = ((IPEndPoint)occupiedListener.LocalEndpoint).Port;
            var input = new ForwarderInput(new ForwarderInputConfiguration {
                Address = IPAddress.Loopback.ToString(),
                Port = port
            });

            var exception = Assert.ThrowsExactly<InvalidOperationException>(() => input.Open(TestContext.CancellationToken).Subscribe(_ => { }));

            Assert.Contains($"{IPAddress.Loopback}:{port}", exception.Message);
            Assert.IsInstanceOfType<SocketException>(exception.InnerException);
        }
        finally
        {
            occupiedListener.Stop();
        }
    }

    [TestMethod]
    public void YamlForwarderOutputConfigurationLoader_LoadFile_RejectsInvalidEndpoint()
    {
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "forwarder.yaml");
        File.WriteAllText(configPath, """
            buffer:
              path: /tmp/dz-buffer
            forwarder:
              endpoints:
                - host: ""
                  port: 6514
            """);

        Assert.ThrowsExactly<InvalidDataException>(() => new YamlForwarderOutputConfigurationLoader().LoadFile(configPath));
    }

    [TestMethod]
    public void YamlForwarderOutputConfigurationLoader_LoadFile_RejectsThumbprintValidationWithoutThumbprints()
    {
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "forwarder.yaml");
        File.WriteAllText(configPath, """
            forwarder:
              useTls: true
              tls:
                certificateValidation: Thumbprint
              endpoints:
                - host: forwarder.example
                  port: 6514
            """);

        Assert.ThrowsExactly<InvalidDataException>(() => new YamlForwarderOutputConfigurationLoader().LoadFile(configPath));
    }

    [TestMethod]
    public void ForwarderOptions_GetConfiguredEndpoints_UsesFailoverListWhenProvided()
    {
        var options = new ForwarderOptions {
            Host = "primary.example",
            Port = 6514,
            Endpoints = [
                new ForwarderEndpoint { Host = "forwarder-a.example", Port = 6514 },
                new ForwarderEndpoint { Host = "forwarder-b.example", Port = 6515 }
            ]
        };

        var endpoints = options.GetConfiguredEndpoints();

        Assert.HasCount(2, endpoints);
        Assert.AreEqual("forwarder-a.example", endpoints[0].Host);
        Assert.AreEqual(6515, endpoints[1].Port);
    }

    [TestMethod]
    public void ForwarderTransportConfiguration_ToForwarderOptions_MapsTlsAndEndpointSettings()
    {
        var configuration = new ForwarderTransportConfiguration {
            UseTls = true,
            Endpoints = [
                new ForwarderEndpoint { Host = "forwarder-a.example", Port = 6514 },
                new ForwarderEndpoint { Host = "forwarder-b.example", Port = 6515 }
            ],
            Tls = new ForwarderTlsConfiguration {
                CertificateValidation = CertificateValidationMode.Thumbprint,
                AllowedServerCertificateThumbprints = ["AA11"],
                CertificateExpiryWarningDays = 14
            }
        };

        var options = configuration.ToForwarderOptions();

        Assert.AreEqual("forwarder-a.example", options.Host);
        Assert.AreEqual(6514, options.Port);
        Assert.IsTrue(options.UseTls);
        Assert.AreEqual(CertificateValidationMode.Thumbprint, options.CertificateValidation);
        Assert.HasCount(1, options.AllowedServerCertificateThumbprints);
        Assert.AreEqual("AA11", options.AllowedServerCertificateThumbprints[0]);
        Assert.AreEqual(14, options.CertificateExpiryWarningDays);
        Assert.HasCount(2, options.GetConfiguredEndpoints());
    }

    [TestMethod]
    public void ForwarderTransportConfiguration_ToForwarderOptions_AppliesTunnelOverrides()
    {
        var configuration = new ForwarderTransportConfiguration {
            UseTls = true,
            Endpoints = [new ForwarderEndpoint { Host = "upstream.example", Port = 6514 }]
        };
        IReadOnlyList<ForwarderEndpoint> tunnelEndpoints = [new ForwarderEndpoint { Host = "127.0.0.1", Port = 2515 }];

        var options = configuration.ToForwarderOptions(endpoints: tunnelEndpoints, useTls: false);

        Assert.AreEqual("127.0.0.1", options.Host);
        Assert.AreEqual(2515, options.Port);
        Assert.AreSame(tunnelEndpoints, options.Endpoints);
        Assert.IsFalse(options.UseTls);
    }

    [TestMethod]
    public void ForwarderTransport_Constructor_RejectsInvalidFailoverEndpoint() => Assert.ThrowsExactly<ArgumentException>(() => new ForwarderTransport(new ForwarderOptions {
        Host = "primary.example",
        Port = 6514,
        Endpoints = [new ForwarderEndpoint { Host = " ", Port = 6514 }]
    }));

    [TestMethod]
    public async Task ForwarderTransport_SendAsync_SendsBatchAndReturnsAcceptedAck()
    {
        using var timeout = CreateForwarderTestTimeout();
        await using var server = await FakeForwarderServer.StartAsync(acceptSyslog: true, timeout.Token);
        await using var transport = new ForwarderTransport(new ForwarderOptions {
            Host = "127.0.0.1",
            Port = server.Port
        });

        var batch = new DeliveryBatch {
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
    public async Task ForwarderTransport_SendAsync_ReturnsRejectedAckWhenForwarderServerRejectsBatch()
    {
        using var timeout = CreateForwarderTestTimeout();
        await using var server = await FakeForwarderServer.StartAsync(acceptSyslog: false, timeout.Token);
        await using var transport = new ForwarderTransport(new ForwarderOptions {
            Host = "127.0.0.1",
            Port = server.Port
        });

        var ack = await transport.SendAsync(new DeliveryBatch {
            BatchId = "batch-02",
            Records = [CreateTestDeliveryRecord()]
        }, timeout.Token).AsTask().WaitAsync(timeout.Token);

        Assert.IsFalse(ack.Accepted);
        Assert.AreEqual("batch-02", ack.BatchId);
        Assert.Contains("Forwarder send failed", ack.Reason!);
    }

    [TestMethod]
    public async Task ForwarderTransport_Dispose_IsIdempotentAcrossSyncAndAsyncCalls()
    {
        var transport = new ForwarderTransport(new ForwarderOptions {
            Host = "127.0.0.1",
            Port = 6514
        });

        transport.Dispose();
        transport.Dispose();
        await transport.DisposeAsync();
    }

    [TestMethod]
    public void ForwarderHealthObservation_ToOutputRecord_ContainsAllExpectedFields()
    {
        var now = DateTimeOffset.UtcNow;
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
                    AvailableChunks = 2,
                    InFlightChunks = 0,
                    MaxInFlightChunks = 4,
                    DispatchQueueDepth = 0,
                    DispatchQueueCapacity = 16,
                    DispatcherWaitReason = DispatchWaitReason.NoAvailableChunks,
                    OldestChunkAge = TimeSpan.FromSeconds(5),
                    OldestAvailableChunkAge = TimeSpan.FromSeconds(5),
                    OldestDispatchedChunkAge = null,
                    RecordsAcceptedTotal = 100,
                    RecordsRejectedTotal = 0,
                    RecordsDroppedTotal = 0,
                    ChunksCompletedTotal = 9,
                    ChunksReleasedTotal = 2,
                    ChunksDeadLetteredTotal = 0,
                    DeadLetterBytesLimit = 10_000,
                    DeadLetterBytesUsed = 0,
                    ChunksDeadLetterEvictedTotal = 0,
                    QuarantineBytesLimit = 10_000,
                    QuarantineBytesUsed = 0,
                    ChunksQuarantineEvictedTotal = 0,
                },
                Transport = new ForwarderTransportSnapshot {
                    SendAttemptsTotal = 12,
                    SendSuccessesTotal = 9,
                    TransientFailuresTotal = 3,
                    PermanentFailuresTotal = 0,
                    ChunksDeadLetteredTotal = 0,
                    ChunksDiscardedTotal = 0,
                    IsRunning = true
                },
                LastForwarderActivityUtc = now
            }
        };

        var record = observation.ToOutputRecord();

        Assert.AreEqual(ForwarderHealthObservation.RecordKind, record.Metadata["recordKind"]);
        Assert.AreEqual("Healthy", record.Event["bufferState"]);
        Assert.AreEqual(2, record.Event["availableChunks"]);
        Assert.AreEqual(0, record.Event["inFlightChunks"]);
        Assert.AreEqual(4, record.Event["maxInFlightChunks"]);
        Assert.AreEqual(0, record.Event["dispatchQueueDepth"]);
        Assert.AreEqual(16, record.Event["dispatchQueueCapacity"]);
        Assert.AreEqual("idle", record.Event["dispatcherWaitReason"]);
        Assert.AreEqual(5000d, record.Event["oldestAvailableChunkAgeMs"]);
        Assert.IsNull(record.Event["oldestDispatchedChunkAgeMs"]);
        Assert.AreEqual(100L, record.Event["recordsAcceptedTotal"]);
        Assert.AreEqual(9L, record.Event["chunksCompletedTotal"]);
        Assert.AreEqual(2L, record.Event["chunksReleasedTotal"]);
        Assert.AreEqual(0L, record.Event["chunksDeadLetteredTotal"]);
        Assert.AreEqual(12L, record.Event["transportSendAttemptsTotal"]);
        Assert.AreEqual(9L, record.Event["transportSendSuccessesTotal"]);
        Assert.AreEqual(3L, record.Event["transportTransientFailuresTotal"]);
        Assert.IsTrue((bool?)record.Event["transportIsRunning"]);
        Assert.AreEqual(now, record.Event["lastForwarderActivityUtc"]);
    }

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

    private CancellationTokenSource CreateForwarderTestTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        return timeout;
    }

    private sealed class FakeForwarderServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly bool _acceptSyslog;
        private readonly Task _serverTask;
        private readonly TaskCompletionSource<DeliveryBatch?> _batchSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _cts = new();
        private TcpClient? _client;

        private FakeForwarderServer(TcpListener listener, bool acceptSyslog)
        {
            _listener = listener;
            _acceptSyslog = acceptSyslog;
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverTask = Task.Run(RunAsync);
        }

        public int Port { get; }

        public static Task<FakeForwarderServer> StartAsync(bool acceptSyslog, CancellationToken cancellationToken)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new FakeForwarderServer(listener, acceptSyslog));
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
                    var maybeFrame = await ForwarderFrameCodec.ReadFrameAsync(stream, _cts.Token).ConfigureAwait(false);
                    if (maybeFrame is not { } frame)
                    {
                        return;
                    }

                    switch (frame.Command)
                    {
                        case "open":
                            await ForwarderFrameCodec.WriteResponseAsync(stream, frame.TransactionId, "200 OK\nrelp_version=0\ncommands=syslog", _cts.Token).ConfigureAwait(false);
                            break;

                        case "syslog":
                            _batchSource.TrySetResult(new MessagePackPayloadSerializer().Deserialize<DeliveryBatch>(frame.Payload));
                            await ForwarderFrameCodec.WriteResponseAsync(stream, frame.TransactionId, _acceptSyslog ? "200 OK" : "500 rejected", _cts.Token).ConfigureAwait(false);
                            if (!_acceptSyslog)
                            {
                                return;
                            }
                            break;

                        case "close":
                            await ForwarderFrameCodec.WriteResponseAsync(stream, frame.TransactionId, "200 OK", _cts.Token).ConfigureAwait(false);
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

        public void OnError(Exception error)
        { }

        public void OnCompleted()
        { }

        public void Dispose()
        { }
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
