using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Agent.Core.Observability;
using DeltaZulu.Buffer;
using DeltaZulu.Buffer.Configuration;
using DeltaZulu.Buffer.Metrics;

namespace DeltaZulu.Agent.Forwarder;

public sealed class BufferedForwarderSink : IResourceSink
{
    private readonly DeltaZuluBufferHost<DeliveryRecord> _host;
    private readonly string _storagePath;
    private readonly CancellationToken _cancellationToken;
    private readonly IDisposable _eventSubscription;
    private long _batchesSent;
    private long _batchesAcknowledged;
    private long _batchesFailed;
    private long _batchesRetryScheduled;
    private long _batchesDeadLettered;
    private long _lastForwarderActivityTicks;
    private bool _disposed;

    public BufferedForwarderSink(
        DeltaZuluBufferOptions options,
        IForwarderTransport transport,
        CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        _storagePath = options.StoragePath;
        _host = new DeltaZuluBufferHost<DeliveryRecord>(
            options,
            new DeliveryRecordSerializer(),
            new ForwarderChunkSender(transport));
        _eventSubscription = _host.Events.Subscribe(new ForwarderBufferEventObserver(RecordBufferEvent));
        _host.StartAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
    }

    public string Name => "buffered-forwarder";

    public ForwarderHealthSnapshot GetHealthSnapshot() => new()
    {
        Buffer = _host.Buffer.GetSnapshot(),
        BatchesSentTotal = Interlocked.Read(ref _batchesSent),
        BatchesAcknowledgedTotal = Interlocked.Read(ref _batchesAcknowledged),
        BatchesFailedTotal = Interlocked.Read(ref _batchesFailed),
        BatchesRetryScheduledTotal = Interlocked.Read(ref _batchesRetryScheduled),
        BatchesDeadLetteredTotal = Interlocked.Read(ref _batchesDeadLettered),
        LastForwarderActivityUtc = ReadLastForwarderActivity()
    };

    public ResourceOutputRecord GetHealthOutputRecord(CollectorObservationMetadata metadata) =>
        new ForwarderHealthObservation
        {
            Metadata = metadata,
            Health = GetHealthSnapshot()
        }.ToOutputRecord();

    public void OnCompleted()
    {
        _host.Buffer.FlushAsync(_cancellationToken).AsTask().GetAwaiter().GetResult();
        WaitForDispatchIdle(_cancellationToken);
    }

    public void OnError(Exception error)
    {
        Console.Error.WriteLine($"forwarder sink error: {error}");
        Console.Error.Flush();
    }

    public void OnNext(ResourceOutputRecord value)
    {
        var result = _host.Buffer.WriteAsync(
            DeliveryRecord.FromResourceOutput(value),
            _cancellationToken).AsTask().GetAwaiter().GetResult();

        if (!result.IsAccepted)
        {
            throw new InvalidOperationException($"Forwarder buffer rejected record: {result.Status}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _host.Buffer.FlushAsync(_cancellationToken).AsTask().GetAwaiter().GetResult();
        WaitForDispatchIdle(_cancellationToken);
        _host.StopAsync(_cancellationToken).AsTask().GetAwaiter().GetResult();
        _eventSubscription.Dispose();
        _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _disposed = true;
    }

    private void RecordBufferEvent(BufferEvent bufferEvent)
    {
        switch (bufferEvent.EventType)
        {
            case BufferEventType.BufferChunkDispatchStarted:
                Interlocked.Increment(ref _batchesSent);
                RecordForwarderActivity(bufferEvent.TimestampUtc);
                break;
            case BufferEventType.BufferChunkDispatchSucceeded:
                Interlocked.Increment(ref _batchesAcknowledged);
                RecordForwarderActivity(bufferEvent.TimestampUtc);
                break;
            case BufferEventType.BufferChunkDispatchFailed:
                Interlocked.Increment(ref _batchesFailed);
                RecordForwarderActivity(bufferEvent.TimestampUtc);
                break;
            case BufferEventType.BufferChunkRetryScheduled:
                Interlocked.Increment(ref _batchesRetryScheduled);
                RecordForwarderActivity(bufferEvent.TimestampUtc);
                break;
            case BufferEventType.BufferChunkDeadLettered:
                Interlocked.Increment(ref _batchesDeadLettered);
                RecordForwarderActivity(bufferEvent.TimestampUtc);
                break;
        }
    }

    private void RecordForwarderActivity(DateTimeOffset timestamp) =>
        Interlocked.Exchange(ref _lastForwarderActivityTicks, timestamp.UtcTicks);

    private DateTimeOffset? ReadLastForwarderActivity()
    {
        var ticks = Interlocked.Read(ref _lastForwarderActivityTicks);
        return ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    }

    private void WaitForDispatchIdle(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasPendingChunks())
            {
                return;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(100));
        }
    }

    private bool HasPendingChunks()
    {
        return HasChunkFiles(Path.Combine(_storagePath, "sealed"))
            || HasChunkFiles(Path.Combine(_storagePath, "dispatching"));
    }

    private static bool HasChunkFiles(string path) =>
        Directory.Exists(path) && Directory.EnumerateFiles(path, "*.chunk").Any();

    private sealed class ForwarderBufferEventObserver : IObserver<BufferEvent>
    {
        private readonly Action<BufferEvent> _onNext;

        public ForwarderBufferEventObserver(Action<BufferEvent> onNext) => _onNext = onNext;

        public void OnCompleted()
        { }

        public void OnError(Exception error)
        { }

        public void OnNext(BufferEvent value) => _onNext(value);
    }
}
