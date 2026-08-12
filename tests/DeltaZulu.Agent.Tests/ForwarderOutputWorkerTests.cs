using System.Threading.Channels;
using DeltaZulu.DurableBuffer;
using DeltaZulu.DurableBuffer.Configuration;
using DeltaZulu.DurableBuffer.Abstractions;
using DeltaZulu.DurableBuffer.Chunks;
using DeltaZulu.Forward;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Delivery;
using DeltaZulu.Pipeline.Outputs.Forwarder;

namespace DeltaZulu.Agent.Tests;

[TestClass]
public sealed class ForwarderOutputWorkerTests
{
    [TestMethod]
    public async Task RunAsync_SendsChunkAndCompletes()
    {
        using var directory = new TemporaryDirectory();
        var reader = new StubBufferReader();
        ForwardLogBatch? sentBatch = null;
        var transport = new CallbackTransport(batch => {
            sentBatch = batch;
            return new DeliveryAck { BatchId = batch.BatchId, Accepted = true };
        });
        var worker = new ForwarderOutputWorker(reader, transport, new ForwarderRetryConfiguration());

        var chunk = await CreateChunkOnDiskAsync(directory.Path);
        await reader.EnqueueAsync(chunk);
        reader.CompleteChannel();

        await worker.RunAsync(TestContext.CancellationToken);

        Assert.HasCount(1, reader.Completed);
        Assert.AreEqual(chunk.Id, reader.Completed[0].Id);
        Assert.IsEmpty(reader.DeadLettered);

        Assert.IsNotNull(sentBatch);
        Assert.AreEqual(Guid.Parse(chunk.Id.Value), sentBatch.BatchId,
            "The batch id sent to the transport must be a deterministic derivation of the chunk id.");

        var snapshot = worker.GetSnapshot();
        Assert.AreEqual(1, snapshot.SendAttemptsTotal);
        Assert.AreEqual(1, snapshot.SendSuccessesTotal);
        Assert.AreEqual(0, snapshot.ChunksDeadLetteredTotal);
    }

    [TestMethod]
    public async Task RunAsync_TransientFailure_RetriesUntilSuccess()
    {
        using var directory = new TemporaryDirectory();
        var reader = new StubBufferReader();
        long attempts = 0;
        var transport = new CallbackTransport(batch => {
            var attempt = Interlocked.Increment(ref attempts);
            return new DeliveryAck {
                BatchId = batch.BatchId,
                Accepted = attempt >= 3,
                Reason = attempt >= 3 ? null : "transient"
            };
        });
        var worker = new ForwarderOutputWorker(reader, transport, new ForwarderRetryConfiguration {
            MaxAttempts = 5,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(10)
        });

        var chunk = await CreateChunkOnDiskAsync(directory.Path);
        await reader.EnqueueAsync(chunk);
        reader.CompleteChannel();

        await worker.RunAsync(TestContext.CancellationToken);

        Assert.AreEqual(3, Interlocked.Read(ref attempts));
        Assert.HasCount(1, reader.Completed);
        Assert.IsEmpty(reader.DeadLettered);
    }

    [TestMethod]
    public async Task RunAsync_RetriesExhausted_DeadLetters()
    {
        using var directory = new TemporaryDirectory();
        var reader = new StubBufferReader();
        long attempts = 0;
        var transport = new CallbackTransport(batch => {
            Interlocked.Increment(ref attempts);
            return new DeliveryAck { BatchId = batch.BatchId, Accepted = false, Reason = "server down" };
        });
        var worker = new ForwarderOutputWorker(reader, transport, new ForwarderRetryConfiguration {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(10)
        });

        var chunk = await CreateChunkOnDiskAsync(directory.Path);
        await reader.EnqueueAsync(chunk);
        reader.CompleteChannel();

        await worker.RunAsync(TestContext.CancellationToken);

        Assert.AreEqual(3, Interlocked.Read(ref attempts));
        Assert.IsEmpty(reader.Completed);
        Assert.HasCount(1, reader.DeadLettered);
        Assert.Contains("Retry exhausted", reader.DeadLettered[0].Reason!);
        Assert.Contains("server down", reader.DeadLettered[0].Reason!);

        var snapshot = worker.GetSnapshot();
        Assert.AreEqual(1, snapshot.ChunksDeadLetteredTotal);
    }

    [TestMethod]
    public async Task RunAsync_RetriesExhaustedWithDiscardPolicy_CompletesChunk()
    {
        using var directory = new TemporaryDirectory();
        var reader = new StubBufferReader();
        var transport = new CallbackTransport(batch =>
            new DeliveryAck { BatchId = batch.BatchId, Accepted = false, Reason = "server down" });
        var worker = new ForwarderOutputWorker(reader, transport, new ForwarderRetryConfiguration {
            MaxAttempts = 2,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(10),
            ExhaustedPolicy = ForwarderRetryExhaustedPolicy.Discard
        });

        var chunk = await CreateChunkOnDiskAsync(directory.Path);
        await reader.EnqueueAsync(chunk);
        reader.CompleteChannel();

        await worker.RunAsync(TestContext.CancellationToken);

        Assert.HasCount(1, reader.Completed);
        Assert.IsEmpty(reader.DeadLettered);
        Assert.AreEqual(1, worker.GetSnapshot().ChunksDiscardedTotal);
    }

    [TestMethod]
    public async Task RunAsync_RecordCountMismatch_DeadLettersWithoutSending()
    {
        using var directory = new TemporaryDirectory();
        var reader = new StubBufferReader();
        long attempts = 0;
        var transport = new CallbackTransport(batch => {
            Interlocked.Increment(ref attempts);
            return new DeliveryAck { BatchId = batch.BatchId, Accepted = true };
        });
        var worker = new ForwarderOutputWorker(reader, transport, new ForwarderRetryConfiguration());

        var chunk = await CreateChunkOnDiskAsync(directory.Path, declaredRecordCount: 99);
        await reader.EnqueueAsync(chunk);
        reader.CompleteChannel();

        await worker.RunAsync(TestContext.CancellationToken);

        Assert.AreEqual(0, Interlocked.Read(ref attempts));
        Assert.IsEmpty(reader.Completed);
        Assert.HasCount(1, reader.DeadLettered);
        Assert.Contains("expected 99 records but decoded 1", reader.DeadLettered[0].Reason!);
        Assert.AreEqual(1, worker.GetSnapshot().PermanentFailuresTotal);
    }

    [TestMethod]
    public async Task RunAsync_Cancellation_StopsWorker()
    {
        var reader = new StubBufferReader();
        var transport = new CallbackTransport(batch =>
            new DeliveryAck { BatchId = batch.BatchId, Accepted = true });
        var worker = new ForwarderOutputWorker(reader, transport, new ForwarderRetryConfiguration());

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var runTask = worker.RunAsync(cts.Token);

        await cts.CancelAsync();
        await runTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);

        Assert.IsFalse(worker.GetSnapshot().IsRunning);
    }

    private static async Task<StoredChunk> CreateChunkOnDiskAsync(
        string directoryPath,
        int declaredRecordCount = 1)
    {
        await using var host = new DurableBufferHost<ForwardLogRecord>(
            new DurableBufferOptions {
                StoragePath = directoryPath,
                MaxChunkRecords = 1,
                MaxChunkBytes = 4096,
                MaxChunkAge = TimeSpan.FromMinutes(5)
            },
            new ForwarderDeliveryRecordSerializer());

        await host.StartAsync(CancellationToken.None);
        var result = await host.Writer.WriteAsync(CreateRecord(), CancellationToken.None);
        Assert.IsTrue(result.IsAccepted, $"Expected durable buffer to accept the test record, but got {result.Status}.");

        var chunk = await host.Reader.SealedChunks.ReadAsync(CancellationToken.None);
        if (declaredRecordCount == chunk.Metadata.RecordCount)
        {
            return chunk;
        }

        return new StoredChunk {
            Id = chunk.Id,
            ChunkFilePath = chunk.ChunkFilePath,
            MetadataFilePath = chunk.MetadataFilePath,
            Metadata = new ChunkMetadata {
                ChunkId = chunk.Metadata.ChunkId,
                CreatedUtc = chunk.Metadata.CreatedUtc,
                SealedUtc = chunk.Metadata.SealedUtc,
                RecordCount = declaredRecordCount,
                PayloadBytes = chunk.Metadata.PayloadBytes,
                Checksum = chunk.Metadata.Checksum
            }
        };
    }

    private static ForwardLogRecord CreateRecord() => new() {
        DeliveryId = Guid.NewGuid().ToString("N"),
        AgentId = "agent-01",
        SourceType = "Syslog",
        SourceName = "auth.log",
        RecordId = "42",
        CreatedAt = DateTimeOffset.UtcNow,
        Fields = new Dictionary<string, object?> { ["Message"] = "hello" }
    };

    private sealed class StubBufferReader : IDurableBufferReader
    {
        private readonly Channel<StoredChunk> _channel = Channel.CreateUnbounded<StoredChunk>();

        public List<StoredChunk> Completed { get; } = [];
        public List<StoredChunk> Released { get; } = [];
        public List<(StoredChunk Chunk, string? Reason)> DeadLettered { get; } = [];

        public ChannelReader<StoredChunk> SealedChunks => _channel.Reader;

        public ValueTask EnqueueAsync(StoredChunk chunk) => _channel.Writer.WriteAsync(chunk);

        public void CompleteChannel() => _channel.Writer.TryComplete();

        public ValueTask CompleteAsync(StoredChunk chunk, CancellationToken cancellationToken = default)
        {
            Completed.Add(chunk);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(StoredChunk chunk, CancellationToken cancellationToken = default)
        {
            Released.Add(chunk);
            return ValueTask.CompletedTask;
        }

        public ValueTask DeadLetterAsync(StoredChunk chunk, string? reason = null, CancellationToken cancellationToken = default)
        {
            DeadLettered.Add((chunk, reason));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CallbackTransport : IDeliveryTransport
    {
        private readonly Func<ForwardLogBatch, DeliveryAck> _handler;

        public CallbackTransport(Func<ForwardLogBatch, DeliveryAck> handler)
        {
            _handler = handler;
        }

        public ValueTask<DeliveryAck> SendAsync(ForwardLogBatch batch, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_handler(batch));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "deltazulu-worker-tests", Guid.NewGuid().ToString("N"));
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
