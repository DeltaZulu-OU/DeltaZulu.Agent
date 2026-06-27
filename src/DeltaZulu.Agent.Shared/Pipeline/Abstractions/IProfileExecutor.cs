using DeltaZulu.Agent.Shared.Pipeline.Events;
using DeltaZulu.Agent.Shared.Pipeline.Profiles;

namespace DeltaZulu.Agent.Shared.Pipeline.Abstractions;

public interface IProfileExecutor : IDisposable
{
    IObservable<ResourceOutputRecord> Execute(
        IObservable<SourceEvent> source,
        ResourceProfile profile,
        CancellationToken cancellationToken = default);
}
