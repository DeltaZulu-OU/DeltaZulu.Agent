using System.Collections.Concurrent;

namespace DeltaZulu.Pipeline.Core.Etw;

public sealed class ThreadIdentityResolver
{
    private readonly ConcurrentDictionary<int, int> _threadToProcess = new();

    public void ObserveThreadStart(int threadId, int processId)
    {
        if (threadId <= 0 || processId <= 0)
        {
            return;
        }

        _threadToProcess[threadId] = processId;
    }

    public void ObserveThreadStop(int threadId) => _threadToProcess.TryRemove(threadId, out _);

    public int? ResolveProcessId(int threadId) =>
        _threadToProcess.TryGetValue(threadId, out var processId) ? processId : null;
}
