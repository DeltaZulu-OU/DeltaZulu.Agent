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
    private readonly IForwarderTransport _transport;
    private readonly CancellationToken _cancellationToken;
    private readonly IDisposable _activitySubscription;
    private long _lastForwarderActivityTicks;
    private bool _disposed;

    public BufferedForwarderSink(
        DeltaZuluBufferOptions options,
        IForwarderTransport transport,
        CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        _transport = transport;
        _host = new DeltaZuluBufferHost<DeliveryRecord>(
            options,
            new DeliveryRecordSerializer(),
            new ForwarderChunkSender(_transport));
        _activitySubscription = _host.Events.Subscribe(new ActivityTimestampObserver(RecordActivity));
        _host.StartAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
    }

    public string Name => "buffered-forwarder";

    public ForwarderHealthSnapshot GetHealthSnapshot() => new()
    {
        Buffer = _host.Buffer.GetSnapshot(),
        LastForwarderActivityUtc = ReadLastActivity()
    };

    public ResourceOutputRecord GetHealthOutputRecord(CollectorObservationMetadata metadata) =>
        new ForwarderHealthObservation
        {
            Metadata = metadata,
            Health = GetHealthSnapshot()
        }.ToOutputRecord();

    public void OnNext(ResourceOutputRecord value)
    {
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
        if (_disposed)
        {
            return;
        }

        _host.StopAsync(_cancellationToken).AsTask().GetAwaiter().GetResult();
        _activitySubscription.Dispose();
        _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        DisposeTransport();
        _disposed = true;
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

        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
