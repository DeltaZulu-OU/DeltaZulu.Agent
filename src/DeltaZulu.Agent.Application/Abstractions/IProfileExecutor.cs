using DeltaZulu.Agent.Domain.Events;
using DeltaZulu.Agent.Domain.Profiles;

namespace DeltaZulu.Agent.Application.Abstractions;

public interface IProfileExecutor : IDisposable
{
    IObservable<ResourceOutputRecord> Execute(
        IObservable<SourceEvent> source,
        ResourceProfile profile,
        CancellationToken cancellationToken = default);
}
