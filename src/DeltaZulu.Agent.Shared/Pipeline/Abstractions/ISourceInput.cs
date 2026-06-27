using DeltaZulu.Agent.Shared.Pipeline.Events;

namespace DeltaZulu.Agent.Shared.Pipeline.Abstractions;

public interface ISourceInput
{
    string Name { get; }

    IObservable<SourceEvent> Open(CancellationToken cancellationToken = default);
}
