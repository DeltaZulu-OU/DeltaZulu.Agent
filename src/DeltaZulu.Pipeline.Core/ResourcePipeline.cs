using System.Reactive.Disposables;
using System.Reactive.Linq;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Core;

public sealed class ResourcePipeline
{
    private readonly ISourceInput _input;
    private readonly Func<IObservable<SourceEvent>, IObservable<ResourceOutputRecord>> _executeProfiles;
    private readonly IOutputWriter _sink;
    private readonly AgentObservationAccumulator? _observations;

    public ResourcePipeline(
        ISourceInput input,
        Func<IObservable<SourceEvent>, IObservable<ResourceOutputRecord>> executeProfiles,
        IOutputWriter sink,
        AgentObservationAccumulator? observations = null)
    {
        _input = input;
        _executeProfiles = executeProfiles;
        _sink = sink;
        _observations = observations;
    }

    public IDisposable Start(CancellationToken cancellationToken = default)
    {
        var source = _input.Open(cancellationToken);
        if (_observations is not null)
        {
            source = source.Do(_observations.RecordRead);
        }

        var output = _executeProfiles(source);
        if (_observations is not null)
        {
            output = output.Do(_observations.RecordKeptAfterFilter);
        }

        IObserver<ResourceOutputRecord> observer = _observations is null
            ? _sink
            : new ForwardingObservationSink(_sink, _observations);
        var subscription = output.Subscribe(observer);
        return Disposable.Create(() => {
            subscription.Dispose();
            _sink.Dispose();
        });
    }

    private sealed class ForwardingObservationSink : IObserver<ResourceOutputRecord>
    {
        private readonly IOutputWriter _inner;
        private readonly AgentObservationAccumulator _observations;

        public ForwardingObservationSink(IOutputWriter inner, AgentObservationAccumulator observations)
        {
            _inner = inner;
            _observations = observations;
        }

        public void OnCompleted() => _inner.OnCompleted();

        public void OnError(Exception error) => _inner.OnError(error);

        public void OnNext(ResourceOutputRecord value)
        {
            try
            {
                _inner.OnNext(value);
                _observations.RecordForwarded(value);
            }
            catch
            {
                _observations.RecordForwardFailed(value);
                throw;
            }
        }
    }
}