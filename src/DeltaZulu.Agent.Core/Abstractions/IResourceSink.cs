using DeltaZulu.Agent.Core.Events;

namespace DeltaZulu.Agent.Core.Abstractions;

public interface IResourceSink : IObserver<ResourceOutputRecord>, IDisposable
{
    string Name { get; }
}