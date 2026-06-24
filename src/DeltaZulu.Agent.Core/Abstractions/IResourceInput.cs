using DeltaZulu.Agent.Core.Events;

namespace DeltaZulu.Agent.Core.Abstractions;

/// <summary>
/// Library-level input abstraction. Daemons, CLIs, services, and tests can all host inputs.
/// </summary>
public interface IResourceInput
{
    string Name { get; }

    IObservable<SourceEvent> Open(CancellationToken cancellationToken = default);
}