using DeltaZulu.Pipeline.Core.Events;
using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Pipeline.Core.Abstractions;

public interface IProfileExecutor : IDisposable
{
    IObservable<ResourceOutputRecord> Execute(
        IObservable<SourceEvent> source,
        ResourceProfile profile,
        CancellationToken cancellationToken = default);
}