using DeltaZulu.Agent.Shared.Pipeline.Abstractions;
using DeltaZulu.Agent.Shared.Pipeline.Events;

namespace DeltaZulu.Agent.Shared.Orchestrator.Runtime;

public sealed class CompletionTrackingWriter : IOutputWriter
{
    private readonly ManualResetEventSlim _completed;
    private readonly IOutputWriter _inner;
    private readonly bool _completeInner;
    private readonly Lock _lock = new();

    public CompletionTrackingWriter(IOutputWriter inner, ManualResetEventSlim completed, bool completeInner = true)
    {
        (_inner, _completed, _completeInner) = (inner, completed, completeInner);
    }

    public string Name => _inner.Name;

    public Exception? Error {
        get {
            lock (_lock)
            {
                return field;
            }
        }
        private set;
    }

    public void Dispose() { }

    public void OnCompleted()
    {
        try
        {
            if (_completeInner)
            {
                _inner.OnCompleted();
            }
        }
        finally
        {
            _completed.Set();
        }
    }

    public void OnError(Exception error)
    {
        lock (_lock)
        {
            Error ??= error;
        }

        try
        {
            if (_completeInner)
            {
                _inner.OnError(error);
            }
        }
        finally
        {
            _completed.Set();
        }
    }

    public void OnNext(ResourceOutputRecord value) => _inner.OnNext(value);
}
