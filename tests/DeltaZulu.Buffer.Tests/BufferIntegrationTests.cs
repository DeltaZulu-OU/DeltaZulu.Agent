using System.Text;
using DeltaZulu.Buffer.Abstractions;
using DeltaZulu.Buffer.Chunks;
using DeltaZulu.Buffer.Configuration;
using DeltaZulu.Buffer.Dispatch;
using DeltaZulu.Buffer.Metrics;

namespace DeltaZulu.Buffer.Tests;

[TestClass]
public sealed class BufferIntegrationTests
{
    private string _storagePath = null!;

    [TestInitialize]
    public void Setup() => _storagePath = Path.Combine(Path.GetTempPath(), $"dz_integ_{Guid.NewGuid():N}");

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_storagePath, true); }
        catch { }
    }

    private sealed class SuccessSender : IChunkSender
    {
        public int SendCount;
        public List<StoredChunk> SentChunks { get; } = [];

        public ValueTask<ChunkSendResult> SendAsync(StoredChunk chunk, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref SendCount);
            lock (SentChunks)
            {
                SentChunks.Add(chunk);
            }

            return ValueTask.FromResult(new ChunkSendResult(ChunkSendStatus.Success));
        }
    }

    private sealed class FailingSender : IChunkSender
    {
        public int AttemptCount;
        private readonly ChunkSendStatus _status;

        public FailingSender(ChunkSendStatus status = ChunkSendStatus.TransientFailure) => _status = status;

        public ValueTask<ChunkSendResult> SendAsync(StoredChunk chunk, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref AttemptCount);
            return ValueTask.FromResult(new ChunkSendResult(_status, "simulated failure"));
        }
    }

    [TestMethod]
    public async Task WriteAndDispatch_EndToEnd()
    {
        var sender = new SuccessSender();
        var options = new DeltaZuluBufferOptions
        {
            StoragePath = _storagePath,
            MaxChunkRecords = 2,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromSeconds(30)
        };

        await using var host = new DeltaZuluBufferHost<string>(options, new JsonRecordSerializer<string>(), sender);
        await host.StartAsync();

        var r1 = await host.Buffer.WriteAsync("record1");
        var r2 = await host.Buffer.WriteAsync("record2");

        Assert.AreEqual(BufferWriteStatus.Accepted, r1.Status);
        Assert.AreEqual(BufferWriteStatus.Accepted, r2.Status);

        await Task.Delay(500);
        await host.StopAsync();

        Assert.IsGreaterThanOrEqualTo(1, sender.SendCount);
    }

    [TestMethod]
    public async Task Write_FlushOnStop_DeliversPendingChunk()
    {
        var sender = new SuccessSender();
        var options = new DeltaZuluBufferOptions
        {
            StoragePath = _storagePath,
            MaxChunkRecords = 100,
            MaxChunkBytes = 1024 * 1024,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        await using var host = new DeltaZuluBufferHost<string>(options, new JsonRecordSerializer<string>(), sender);
        await host.StartAsync();

        await host.Buffer.WriteAsync("pending1");
        await host.Buffer.WriteAsync("pending2");

        await host.StopAsync();

        Assert.IsGreaterThanOrEqualTo(1, sender.SendCount);
    }

    [TestMethod]
    public async Task GetSnapshot_ReflectsState()
    {
        var sender = new SuccessSender();
        var options = new DeltaZuluBufferOptions
        {
            StoragePath = _storagePath,
            MaxChunkRecords = 100,
            MaxChunkBytes = 1024 * 1024,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        await using var host = new DeltaZuluBufferHost<string>(options, new JsonRecordSerializer<string>(), sender);
        await host.StartAsync();

        await host.Buffer.WriteAsync("test");
        var snapshot = host.Buffer.GetSnapshot();

        Assert.IsGreaterThanOrEqualTo(1, snapshot.RecordsAcceptedTotal);
        Assert.IsGreaterThan(0, snapshot.OpenChunkBytes);

        await host.StopAsync();
    }

    [TestMethod]
    public async Task Events_ReceivesStartAndStopEvents()
    {
        var sender = new SuccessSender();
        var options = new DeltaZuluBufferOptions
        {
            StoragePath = _storagePath,
            MaxChunkRecords = 100,
            MaxChunkBytes = 1024 * 1024,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        var events = new List<BufferEvent>();

        await using var host = new DeltaZuluBufferHost<string>(options, new JsonRecordSerializer<string>(), sender);
        host.Events.Subscribe(new EventCollector(events));

        await host.StartAsync();
        await host.StopAsync();

        Assert.Contains(e => e.EventType == BufferEventType.BufferStarted, events);
        Assert.Contains(e => e.EventType == BufferEventType.BufferStopped, events);
    }

    [TestMethod]
    public async Task Write_RejectedStopping_AfterStop()
    {
        var sender = new SuccessSender();
        var options = new DeltaZuluBufferOptions
        {
            StoragePath = _storagePath,
            MaxChunkRecords = 100,
            MaxChunkBytes = 1024 * 1024,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        await using var host = new DeltaZuluBufferHost<string>(options, new JsonRecordSerializer<string>(), sender);
        await host.StartAsync();
        await host.StopAsync();

        var result = await host.Buffer.WriteAsync("too-late");
        Assert.AreEqual(BufferWriteStatus.RejectedStopping, result.Status);
    }

    [TestMethod]
    public async Task PermanentFailure_MovesToDeadLetter()
    {
        var sender = new FailingSender(ChunkSendStatus.PermanentFailure);
        var options = new DeltaZuluBufferOptions
        {
            StoragePath = _storagePath,
            MaxChunkRecords = 1,
            MaxChunkBytes = 4096,
            MaxChunkAge = TimeSpan.FromSeconds(30)
        };

        var events = new List<BufferEvent>();

        await using var host = new DeltaZuluBufferHost<string>(options, new JsonRecordSerializer<string>(), sender);
        host.Events.Subscribe(new EventCollector(events));

        await host.StartAsync();
        await host.Buffer.WriteAsync("doomed");
        await Task.Delay(1000);
        await host.StopAsync();

        Assert.Contains(e => e.EventType == BufferEventType.BufferChunkDeadLettered, events);
        var deadLetterDir = Path.Combine(_storagePath, "deadletter");
        Assert.IsTrue(Directory.Exists(deadLetterDir));
        Assert.IsNotEmpty(Directory.EnumerateFiles(deadLetterDir, "*.chunk"));
    }

    [TestMethod]
    public async Task RecordTooLarge_IsRejected()
    {
        var sender = new SuccessSender();
        var options = new DeltaZuluBufferOptions
        {
            StoragePath = _storagePath,
            MaxChunkRecords = 100,
            MaxChunkBytes = 200,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        await using var host = new DeltaZuluBufferHost<string>(options, new JsonRecordSerializer<string>(), sender);
        await host.StartAsync();

        var largeRecord = new string('x', 300);
        var result = await host.Buffer.WriteAsync(largeRecord);

        Assert.AreEqual(BufferWriteStatus.RejectedRecordTooLarge, result.Status);

        await host.StopAsync();
    }

    private sealed class EventCollector(List<BufferEvent> events) : IObserver<BufferEvent>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(BufferEvent value) { lock (events)
            {
                events.Add(value);
            }
        }
    }
}
