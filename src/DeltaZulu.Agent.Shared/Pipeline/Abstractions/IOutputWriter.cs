using DeltaZulu.Agent.Shared.Pipeline.Events;

namespace DeltaZulu.Agent.Shared.Pipeline.Abstractions;

public interface IOutputWriter : IObserver<ResourceOutputRecord>, IDisposable
{
    string Name { get; }
}
