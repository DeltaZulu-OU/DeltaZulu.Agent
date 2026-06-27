using DeltaZulu.Agent.Application.Abstractions;
using DeltaZulu.Agent.Domain.Observability;

namespace DeltaZulu.Agent.Outputs.Relp;

public sealed class RelpHealthReporter : IDisposable
{
    private readonly BufferedRelpSink _forwarder;
    private readonly IOutputWriter _diagnosticSink;
    private readonly CollectorObservationMetadata _metadata;
    private readonly Timer _timer;
    private bool _disposed;

    public RelpHealthReporter(
        BufferedRelpSink forwarder,
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
