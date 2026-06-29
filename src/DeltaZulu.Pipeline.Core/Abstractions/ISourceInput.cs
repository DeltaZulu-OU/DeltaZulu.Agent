using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Core.Abstractions;

public interface ISourceInput
{
    string Name { get; }

    IObservable<SourceEvent> Open(CancellationToken cancellationToken = default);
}