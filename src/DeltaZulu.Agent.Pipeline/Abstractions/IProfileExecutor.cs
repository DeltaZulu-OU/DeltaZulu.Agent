using DeltaZulu.Agent.Pipeline.Events;
using DeltaZulu.Agent.Pipeline.Profiles;

namespace DeltaZulu.Agent.Pipeline.Abstractions;

public interface IProfileExecutor : IDisposable
{
    IObservable<ResourceOutputRecord> Execute(
        IObservable<SourceEvent> source,
        ResourceProfile profile,
        CancellationToken cancellationToken = default);
}
