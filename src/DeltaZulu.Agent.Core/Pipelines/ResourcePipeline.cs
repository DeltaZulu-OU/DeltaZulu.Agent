using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;
using System.Reactive.Disposables;

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

    public ResourcePipeline(
        IResourceInput input,
        Func<IObservable<SourceEvent>, IObservable<ResourceOutputRecord>> executeProfiles,
        IResourceSink sink)
    {
        _input = input;
        _executeProfiles = executeProfiles;
        _sink = sink;
    }

    public IDisposable Start(CancellationToken cancellationToken = default)
    {
        var source = _input.Open(cancellationToken);
        var output = _executeProfiles(source);
        var subscription = output.Subscribe(_sink);
        return Disposable.Create(() =>
        {
            subscription.Dispose();
            _sink.Dispose();
        });
    }
}