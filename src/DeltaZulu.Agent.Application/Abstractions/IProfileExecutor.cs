using DeltaZulu.Agent.Core.Events;
using DeltaZulu.Agent.Profiles;

namespace DeltaZulu.Agent.Application.Abstractions;

public interface IProfileExecutor : IDisposable
{
    IObservable<ResourceOutputRecord> Execute(
        IObservable<SourceEvent> source,
        ResourceProfile profile,
        CancellationToken cancellationToken = default);
}
