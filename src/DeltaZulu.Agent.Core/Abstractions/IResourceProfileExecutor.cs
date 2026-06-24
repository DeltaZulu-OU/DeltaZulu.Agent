using DeltaZulu.Agent.Core.Events;

namespace DeltaZulu.Agent.Core.Abstractions;

public interface IResourceProfileExecutor
{
    IObservable<ResourceOutputRecord> Execute(
        IObservable<SourceEvent> source,
        object profile,
        CancellationToken cancellationToken = default);
}