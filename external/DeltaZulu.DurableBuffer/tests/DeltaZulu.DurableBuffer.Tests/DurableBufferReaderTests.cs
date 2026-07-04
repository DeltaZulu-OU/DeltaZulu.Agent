using System.Text;
using System.Threading.Channels;
using DeltaZulu.DurableBuffer.Chunks;
using DeltaZulu.DurableBuffer.Configuration;
using DeltaZulu.DurableBuffer.Metrics;
using DeltaZulu.DurableBuffer.Storage;

namespace DeltaZulu.DurableBuffer.Tests;

[TestClass]
public sealed class DurableBufferReaderTests
{
    private string _basePath = null!;
    private FileChunkStore _store = null!;
    private BufferMetricsCounter _metrics = null!;
    private BufferEventBroadcaster _events = null!;
    private Channel<StoredChunk> _channel = null!;
    private DurableBufferReader _reader = null!;
    private int _spaceSignals;

    [TestInitialize]
    public void Setup()
    {
        _basePath = Path.Combine(Path.GetTempPath(), $"dz_reader_{Guid.NewGuid():N}");
        _store = new FileChunkStore(_basePath);
        _metrics = new BufferMetricsCounter();
        _events = new BufferEventBroadcaster();
        _channel = Channel.CreateUnbounded<StoredChunk>();
        _spaceSignals = 0;
        _reader = new DurableBufferReader(
            _channel.Reader, _channel.Writer, _store, _metrics, _events,
            () => Interlocked.Increment(ref _spaceSignals));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_basePath, true); }
        catch { }
    }

    private async Task<StoredChunk> SealChunkAsync(string record = "hello")
    {
        var options = new DurableBufferOptions
        {
            StoragePath = _basePath,
            MaxChunkRecords = 1000,
            MaxChunkBytes = 1024 * 1024,
            MaxChunkAge = TimeSpan.FromMinutes(5)
        };

        var chunkId = ChunkId.NewChunkId();
        using var builder = new ChunkBuilder(options);
        builder.Append(Encoding.UTF8.GetBytes(record));
        var (data, meta) = builder.Seal();
        return await _store.SealAsync(chunkId, data, meta with { ChunkId = chunkId.Value }, TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task SealedChunks_ExposesChannelReader()
    {
        var chunk = await SealChunkAsync();
        await _channel.Writer.WriteAsync(chunk, TestContext.CancellationToken);

        var received = await _reader.SealedChunks.ReadAsync(TestContext.CancellationToken);

        Assert.AreEqual(chunk.Id, received.Id);
    }

    [TestMethod]
    public async Task CompleteAsync_DeletesChunkAndSignalsSpace()
    {
        var chunk = await SealChunkAsync();
        _metrics.AddDiskBytes(chunk.Metadata.PayloadBytes);

        await _reader.CompleteAsync(chunk, TestContext.CancellationToken);

        Assert.IsFalse(File.Exists(chunk.ChunkFilePath));
        Assert.IsFalse(File.Exists(chunk.MetadataFilePath));
        Assert.AreEqual(1, Volatile.Read(ref _spaceSignals));

        var snapshot = _metrics.ToSnapshot();
        Assert.AreEqual(1, snapshot.ChunksCompletedTotal);
    }

    [TestMethod]
    public async Task CompleteAsync_PublishesCompletedEvent()
    {
        var received = new List<BufferEvent>();
        _events.Subscribe(new EventCollector(received));
        var chunk = await SealChunkAsync();

        await _reader.CompleteAsync(chunk, TestContext.CancellationToken);

        Assert.Contains(e => e.EventType == BufferEventType.BufferChunkCompleted, received);
    }

    [TestMethod]
    public async Task ReleaseAsync_RequeuesChunkForRedelivery()
    {
        var chunk = await SealChunkAsync();

        await _reader.ReleaseAsync(chunk, TestContext.CancellationToken);

        var requeued = await _reader.SealedChunks.ReadAsync(TestContext.CancellationToken);
        Assert.AreEqual(chunk.Id, requeued.Id);
        Assert.IsTrue(File.Exists(chunk.ChunkFilePath));

        var snapshot = _metrics.ToSnapshot();
        Assert.AreEqual(1, snapshot.ChunksReleasedTotal);
    }

    [TestMethod]
    public async Task DeadLetterAsync_MovesChunkToDeadLetter()
    {
        var chunk = await SealChunkAsync();
        _metrics.AddDiskBytes(chunk.Metadata.PayloadBytes);

        await _reader.DeadLetterAsync(chunk, "simulated failure", TestContext.CancellationToken);

        Assert.IsFalse(File.Exists(chunk.ChunkFilePath));
        Assert.IsNotEmpty(Directory.EnumerateFiles(Path.Combine(_basePath, "deadletter"), "*.chunk"));
        Assert.AreEqual(1, Volatile.Read(ref _spaceSignals));

        var snapshot = _metrics.ToSnapshot();
        Assert.AreEqual(1, snapshot.ChunksDeadLetteredTotal);
    }

    [TestMethod]
    public async Task DeadLetterAsync_PublishesEventWithReason()
    {
        var received = new List<BufferEvent>();
        _events.Subscribe(new EventCollector(received));
        var chunk = await SealChunkAsync();

        await _reader.DeadLetterAsync(chunk, "decode failed", TestContext.CancellationToken);

        var evt = received.Single(e => e.EventType == BufferEventType.BufferChunkDeadLettered);
        Assert.AreEqual("decode failed", evt.Detail);
    }

    private sealed class EventCollector(List<BufferEvent> events) : IObserver<BufferEvent>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(BufferEvent value)
        {
            lock (events)
            {
                events.Add(value);
            }
        }
    }

    public TestContext TestContext { get; set; }
}
