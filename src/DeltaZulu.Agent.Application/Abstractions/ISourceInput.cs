using DeltaZulu.Agent.Domain.Events;

namespace DeltaZulu.Agent.Application.Abstractions;

public interface ISourceInput
{
    string Name { get; }

    IObservable<SourceEvent> Open(CancellationToken cancellationToken = default);
}
