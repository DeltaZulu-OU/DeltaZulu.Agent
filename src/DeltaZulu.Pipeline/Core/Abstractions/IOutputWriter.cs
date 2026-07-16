using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Core.Abstractions;

public interface IOutputWriter : IObserver<ResourceOutputRecord>, IDisposable
{
    string Name { get; }
}
