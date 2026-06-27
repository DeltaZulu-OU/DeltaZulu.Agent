using DeltaZulu.Agent.Pipeline.Events;

namespace DeltaZulu.Agent.Pipeline.Abstractions;

public interface ISourceInput
{
    string Name { get; }

    IObservable<SourceEvent> Open(CancellationToken cancellationToken = default);
}
