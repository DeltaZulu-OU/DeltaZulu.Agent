using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Core;

public sealed class CompletionTrackingWriter : IOutputWriter
{
    private readonly ManualResetEventSlim _completed;
    private readonly bool _completeInner;
    private readonly IOutputWriter _inner;
    private readonly Lock _lock = new();

    public CompletionTrackingWriter(IOutputWriter inner, ManualResetEventSlim completed, bool completeInner = true)
    {
        (_inner, _completed, _completeInner) = (inner, completed, completeInner);
    }

    public Exception? Error {
        get {
            lock (_lock)
            {
                return field;
            }
        }
        private set;
    }

    public string Name => _inner.Name;

    public void Dispose()
    { }

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
