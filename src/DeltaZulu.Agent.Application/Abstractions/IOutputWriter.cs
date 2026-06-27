using DeltaZulu.Agent.Core.Events;

namespace DeltaZulu.Agent.Application.Abstractions;

public interface IOutputWriter : IObserver<ResourceOutputRecord>, IDisposable
{
    string Name { get; }
}
