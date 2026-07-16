using DeltaZulu.DurableBuffer;
using DeltaZulu.DurableBuffer.Configuration;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Delivery;
using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Observability;

namespace DeltaZulu.Pipeline.Outputs.Relp;

public sealed class BufferedRelpSink : IOutputWriter
{
    private static readonly TimeSpan WorkerDrainTimeout = TimeSpan.FromSeconds(30);

    private readonly CancellationToken _cancellationToken;
    private readonly DurableBufferHost<DeliveryRecord> _host;
    private readonly IDeliveryTransport _transport;
    private readonly RelpOutputWorker _worker;
    private readonly CancellationTokenSource _workerCts;
    private readonly Task _workerTask;
    private int _disposed;
    private long _lastForwarderActivityTicks;
    private int _stopped;

    public BufferedRelpSink(
        DurableBufferOptions options,
        IDeliveryTransport transport,
        RelpRetryConfiguration? retryConfiguration = null,
        CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        _transport = transport;
        _host = new DurableBufferHost<DeliveryRecord>(
            options,
            new RelpDeliveryRecordSerializer());
        _worker = new RelpOutputWorker(
            _host.Reader,
            transport,
            retryConfiguration ?? new RelpRetryConfiguration(),
            RecordActivity);
        // The worker must be consuming before StartAsync runs recovery: recovered
        // chunks are pushed into the bounded channel and would deadlock startup
        // if nothing is draining it.
        _workerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _workerTask = Task.Run(() => _worker.RunAsync(_workerCts.Token), CancellationToken.None);
        try
        {
            _host.StartAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            _workerCts.Cancel();
            _workerCts.Dispose();
            throw;
        }
    }

    public string Name => "buffered-relp";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Stop();
        _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _workerCts.Dispose();
        DisposeTransport();
    }

    public ResourceOutputRecord GetHealthOutputRecord(CollectorObservationMetadata metadata) =>
        new RelpHealthObservation {
            Metadata = metadata,
            Health = GetHealthSnapshot()
        }.ToOutputRecord();

    public RelpHealthSnapshot GetHealthSnapshot() => new() {
        Buffer = _host.Writer.GetSnapshot(),
        Transport = _worker.GetSnapshot(),
        LastForwarderActivityUtc = ReadLastActivity()
    };

    public void OnCompleted() => Stop();

    public void OnError(Exception error)
    {
        Console.Error.WriteLine($"forwarder sink error: {error}");
        Console.Error.Flush();
    }

    public void OnNext(ResourceOutputRecord value)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var result = _host.Writer.WriteAsync(
            DeliveryRecord.FromResourceOutput(value),
            _cancellationToken).AsTask().GetAwaiter().GetResult();

        if (!result.IsAccepted)
        {
            Console.Error.WriteLine($"forwarder buffer rejected record: {result.Status}");
            Console.Error.Flush();
        }
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

    private DateTimeOffset? ReadLastActivity()
    {
        var ticks = Interlocked.Read(ref _lastForwarderActivityTicks);
        return ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    }

    private void RecordActivity(DateTimeOffset timestamp) =>
        Interlocked.Exchange(ref _lastForwarderActivityTicks, timestamp.UtcTicks);

    private void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        // StopAsync flushes the open chunk and completes the channel; the worker
        // then drains the remaining sealed chunks before its read loop finishes.
        _host.StopAsync(_cancellationToken).AsTask().GetAwaiter().GetResult();

        try
        {
            _workerTask.WaitAsync(WorkerDrainTimeout, _cancellationToken).GetAwaiter().GetResult();
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("forwarder worker did not drain within timeout");
            Console.Error.Flush();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _workerCts.Cancel();
        }
    }
}
