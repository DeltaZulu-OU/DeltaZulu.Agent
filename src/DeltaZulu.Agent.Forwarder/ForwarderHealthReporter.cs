using DeltaZulu.Agent.Application.Abstractions;
using DeltaZulu.Agent.Core.Observability;

namespace DeltaZulu.Agent.Forwarder;

/// <summary>
/// Periodically emits forwarder health snapshots as output records into a diagnostic sink.
/// </summary>
public sealed class ForwarderHealthReporter : IDisposable
{
    private readonly BufferedForwarderSink _forwarder;
    private readonly IOutputWriter _diagnosticSink;
    private readonly CollectorObservationMetadata _metadata;
    private readonly Timer _timer;
    private bool _disposed;

    public ForwarderHealthReporter(
        BufferedForwarderSink forwarder,
        IOutputWriter diagnosticSink,
        CollectorObservationMetadata metadata,
        TimeSpan interval)
    {
        _forwarder = forwarder;
        _diagnosticSink = diagnosticSink;
        _metadata = metadata;
        _timer = new Timer(_ => EmitHealthSnapshot(), null, interval, interval);
    }

    public void EmitHealthSnapshot()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var record = _forwarder.GetHealthOutputRecord(_metadata with { ObservedAt = DateTimeOffset.UtcNow });
            _diagnosticSink.OnNext(record);
        }
        catch
        {
            // Health reporting must never crash the pipeline.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        _diagnosticSink.OnCompleted();
        _diagnosticSink.Dispose();
    }
}
