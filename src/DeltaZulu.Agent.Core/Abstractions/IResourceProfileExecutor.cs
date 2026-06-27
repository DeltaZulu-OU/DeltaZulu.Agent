using DeltaZulu.Agent.Core.Events;

namespace DeltaZulu.Agent.Core.Abstractions;

[Obsolete("Use IProfileExecutor from DeltaZulu.Agent.Application.Abstractions instead.")]
public interface IResourceProfileExecutor
{
    IObservable<ResourceOutputRecord> Execute(
        IObservable<SourceEvent> source,
        object profile,
        CancellationToken cancellationToken = default);
}
