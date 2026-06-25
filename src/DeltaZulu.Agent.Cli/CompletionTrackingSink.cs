using DeltaZulu.Agent.Core.Abstractions;
using DeltaZulu.Agent.Core.Events;

#if WINDOWS
#endif

namespace DeltaZulu.Agent.Cli;

internal static partial class Program
{
    private sealed class CompletionTrackingSink : IResourceSink
    {
        private readonly ManualResetEventSlim _completed;
        private readonly IResourceSink _inner;
        private readonly bool _completeInner;
        private readonly Lock _lock = new();

        public CompletionTrackingSink(IResourceSink inner, ManualResetEventSlim completed, bool completeInner = true)
        {
            (_inner, _completed, _completeInner) = (inner, completed, completeInner);
        }

        public string Name => _inner.Name;

        internal Exception? Error {
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
        { if (_completeInner) { _inner.OnCompleted(); } _completed.Set(); }

        public void OnError(Exception error)
        {
            lock (_lock) // 3. Synchronize the write block
            {
                Error ??= error; // Captures the first error that occurred
            }
            _inner.OnError(error);
            _completed.Set();
        }

        public void OnNext(ResourceOutputRecord value) => _inner.OnNext(value);
    }
}
