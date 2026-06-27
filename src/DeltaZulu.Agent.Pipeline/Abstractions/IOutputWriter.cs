using DeltaZulu.Agent.Pipeline.Events;

namespace DeltaZulu.Agent.Pipeline.Abstractions;

public interface IOutputWriter : IObserver<ResourceOutputRecord>, IDisposable
{
    string Name { get; }
}
