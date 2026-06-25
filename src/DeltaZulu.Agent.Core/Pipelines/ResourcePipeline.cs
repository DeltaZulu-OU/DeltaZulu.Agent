using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Agent.Core.Observability;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace DeltaZulu.Agent.Core.Pipelines;

/// <summary>
/// Small host-neutral helper for wiring inputs to profile execution and sinks.
/// The daemon remains responsible for service lifecycle, config reload, and OS integration.
/// </summary>
public sealed class ResourcePipeline
{
    private readonly IResourceInput _input;
    private readonly Func<IObservable<SourceEvent>, IObservable<ResourceOutputRecord>> _executeProfiles;
    private readonly IResourceSink _sink;
    private readonly AgentObservationAccumulator? _observations;

    public ResourcePipeline(
        IResourceInput input,
        Func<IObservable<SourceEvent>, IObservable<ResourceOutputRecord>> executeProfiles,
        IResourceSink sink,
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
        return Disposable.Create(() =>
        {
            subscription.Dispose();
            _sink.Dispose();
        });
    }

    private sealed class ForwardingObservationSink : IObserver<ResourceOutputRecord>
    {
        private readonly IResourceSink _inner;
        private readonly AgentObservationAccumulator _observations;

        public ForwardingObservationSink(IResourceSink inner, AgentObservationAccumulator observations)
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