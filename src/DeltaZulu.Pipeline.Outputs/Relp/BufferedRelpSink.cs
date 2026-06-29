using DeltaZulu.DurableBuffer;
using DeltaZulu.DurableBuffer.Configuration;
using DeltaZulu.DurableBuffer.Metrics;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Delivery;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Observability;

namespace DeltaZulu.Pipeline.Outputs.Relp;

public sealed class BufferedRelpSink : IOutputWriter
{
    private readonly DurableBufferHost<DeliveryRecord> _host;
    private readonly IDeliveryTransport _transport;
    private readonly CancellationToken _cancellationToken;
    private readonly IDisposable _activitySubscription;
    private long _lastForwarderActivityTicks;
    private int _disposed;

    public BufferedRelpSink(
        DurableBufferOptions options,
        IDeliveryTransport transport,
        CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        _transport = transport;
        _host = new DurableBufferHost<DeliveryRecord>(
            options,
            new RelpDeliveryRecordSerializer(),
            new RelpChunkSender(_transport));
        _activitySubscription = _host.Events.Subscribe(new ActivityTimestampObserver(RecordActivity));
        _host.StartAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
    }

    public string Name => "buffered-relp";

    public RelpHealthSnapshot GetHealthSnapshot() => new() {
        Buffer = _host.Buffer.GetSnapshot(),
        LastForwarderActivityUtc = ReadLastActivity()
    };

    public ResourceOutputRecord GetHealthOutputRecord(CollectorObservationMetadata metadata) =>
        new RelpHealthObservation {
            Metadata = metadata,
            Health = GetHealthSnapshot()
        }.ToOutputRecord();

    public void OnNext(ResourceOutputRecord value)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var result = _host.Buffer.WriteAsync(
            DeliveryRecord.FromResourceOutput(value),
            _cancellationToken).AsTask().GetAwaiter().GetResult();

        if (!result.IsAccepted)
        {
            Console.Error.WriteLine($"forwarder buffer rejected record: {result.Status}");
            Console.Error.Flush();
        }
    }

    public void OnCompleted() => _host.StopAsync(_cancellationToken).AsTask().GetAwaiter().GetResult();

    public void OnError(Exception error)
    {
        Console.Error.WriteLine($"forwarder sink error: {error}");
        Console.Error.Flush();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _host.StopAsync(_cancellationToken).AsTask().GetAwaiter().GetResult();
        _activitySubscription.Dispose();
        _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        DisposeTransport();
    }

    private void RecordActivity(DateTimeOffset timestamp) =>
        Interlocked.Exchange(ref _lastForwarderActivityTicks, timestamp.UtcTicks);

    private DateTimeOffset? ReadLastActivity()
    {
        var ticks = Interlocked.Read(ref _lastForwarderActivityTicks);
        return ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    }

    private void DisposeTransport()
    {
        switch (_transport)
        {
            case IAsyncDisposable asyncDisposable:
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                break;

            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }

    private sealed class ActivityTimestampObserver : IObserver<BufferEvent>
    {
        private readonly Action<DateTimeOffset> _onActivity;

        public ActivityTimestampObserver(Action<DateTimeOffset> onActivity)
        {
            _onActivity = onActivity;
        }

        public void OnNext(BufferEvent value)
        {
            switch (value.EventType)
            {
                case BufferEventType.BufferChunkDispatchStarted:
                case BufferEventType.BufferChunkDispatchSucceeded:
                case BufferEventType.BufferChunkDispatchFailed:
                case BufferEventType.BufferChunkRetryScheduled:
                case BufferEventType.BufferChunkDeadLettered:
                    _onActivity(value.TimestampUtc);
                    break;
            }
        }

        public void OnCompleted()
        { }

        public void OnError(Exception error)
        { }
    }
}