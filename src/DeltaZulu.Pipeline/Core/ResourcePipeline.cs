using System.Reactive.Disposables;
using System.Reactive.Linq;
using DeltaZulu.LocalStream;
using DeltaZulu.Parse;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Core;

public sealed class ResourcePipeline
{
    // ROADMAP.md Phase 1: pipeline references Parse and LocalStream assemblies.
    // These static references ensure the assemblies are loaded at runtime and appear
    // in GetReferencedAssemblies() calls.
    private static readonly Type _parseMarker = typeof(ParseAssemblyMarker);
    private static readonly Type _localStreamTopics = typeof(LocalStreamTopics);

    private readonly Func<ResourceOutputRecord, ResourceOutputRecord> _enrichAfterFilter;
    private readonly Func<IObservable<SourceEvent>, IObservable<ResourceOutputRecord>> _executeProfiles;
    private readonly ISourceInput _input;
    private readonly AgentObservationAccumulator? _observations;
    private readonly IOutputWriter _sink;

    public ResourcePipeline(
        ISourceInput input,
        Func<IObservable<SourceEvent>, IObservable<ResourceOutputRecord>> executeProfiles,
        IOutputWriter sink,
        AgentObservationAccumulator? observations = null,
        Func<ResourceOutputRecord, ResourceOutputRecord>? enrichAfterFilter = null)
    {
        _input = input;
        _executeProfiles = executeProfiles;
        _sink = sink;
        _observations = observations;
        _enrichAfterFilter = enrichAfterFilter ?? (record => record);
    }

    public IDisposable Start(CancellationToken cancellationToken = default)
    {
        var source = _input.Open(cancellationToken);
        if (_observations is not null)
        {
            source = source.Do(_observations.RecordRead);
        }

        // Logstash-style stage order:
        // input -> filter/profile projection -> deterministic enrichment -> output writer.
        // Keep enrichment here rather than in ResourceOutputRecord factories so filters evaluate
        // source-native fields and output plugins receive the final enriched record.
        var filtered = _executeProfiles(source);
        if (_observations is not null)
        {
            filtered = filtered.Do(_observations.RecordKeptAfterFilter);
        }

        var enriched = filtered.Select(_enrichAfterFilter);

        IObserver<ResourceOutputRecord> observer = _observations is null
            ? _sink
            : new ForwardingObservationSink(_sink, _observations);
        var subscription = enriched.Subscribe(observer);
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
