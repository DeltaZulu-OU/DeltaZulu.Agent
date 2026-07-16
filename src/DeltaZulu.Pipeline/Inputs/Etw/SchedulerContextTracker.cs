namespace DeltaZulu.Pipeline.Inputs.Etw;

public sealed class SchedulerContextTracker
{
    private readonly Dictionary<int, CpuRunningState> _cpuStates = [];
    private readonly Lock _gate = new();
    private readonly int _maximumThreadStates;
    private readonly Dictionary<int, ThreadSchedulingState> _threadStates = [];

    public SchedulerContextTracker(int maximumThreadStates = 100_000)
    {
        if (maximumThreadStates <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumThreadStates), "Tracker capacity must be positive.");
        }

        _maximumThreadStates = maximumThreadStates;
    }

    private enum EtwThreadState
    {
        Ready = 1,
        Running = 2
    }

    public bool LastSwitchHadInvalidPreviousState { get; private set; }

    public bool LastSwitchHadInvalidWaitReason { get; private set; }

    public bool LastSwitchHadMismatchedPreviousThread { get; private set; }

    public int ThreadStateCount {
        get {
            lock (_gate)
            {
                return _threadStates.Count;
            }
        }
    }

    public ThreadSchedulingState ObserveCSwitch(
        int cpuId,
        uint? previousThreadId,
        int previousThreadState,
        int? previousWaitReason,
        uint? nextThreadId,
        int nextThreadPriority,
        DateTimeOffset timestampUtc)
    {
        var previous = ThreadIdentity.FromEtwThreadId(previousThreadId);
        var next = ThreadIdentity.FromEtwThreadId(nextThreadId);
        var previousStateName = ThreadStateLookup.GetName(previousThreadState);
        var wait = WaitReasonLookup.Classify(previousWaitReason);

        lock (_gate)
        {
            LastSwitchHadMismatchedPreviousThread = previous.Status == ThreadIdentityStatus.Resolved &&
                _cpuStates.TryGetValue(cpuId, out var running) &&
                running.Thread.ThreadId.HasValue &&
                running.Thread.ThreadId != previous.ThreadId;
            LastSwitchHadInvalidPreviousState = previousStateName is null;
            LastSwitchHadInvalidWaitReason = previousWaitReason.HasValue && wait.ThreadWaitReason is null;

            if (previous.Status == ThreadIdentityStatus.Resolved)
            {
                var previousState = new ThreadSchedulingState(
                    previous.ThreadId,
                    previous.Status,
                    previousStateName,
                    previousThreadState,
                    "CSwitch",
                    timestampUtc,
                    cpuId,
                    previous.ThreadId,
                    next.ThreadId,
                    null,
                    null,
                    wait.ThreadWaitReasonCode,
                    wait.ThreadWaitReason,
                    wait.ThreadWaitCategory,
                    wait.ThreadWasInIoWait);
                StoreThreadState(previousState);
            }

            _cpuStates[cpuId] = new CpuRunningState(next, timestampUtc, nextThreadPriority);
            var nextState = new ThreadSchedulingState(
                next.ThreadId,
                next.Status,
                ThreadStateLookup.GetName((int)EtwThreadState.Running),
                (int)EtwThreadState.Running,
                "CSwitch",
                timestampUtc,
                cpuId,
                previous.ThreadId,
                next.ThreadId,
                null,
                null,
                null,
                null,
                ThreadWaitCategory.Unknown,
                false);

            if (next.Status == ThreadIdentityStatus.Resolved)
            {
                StoreThreadState(nextState);
            }

            return previous.Status == ThreadIdentityStatus.Resolved ? _threadStates[previous!.ThreadId!.Value] : nextState;
        }
    }

    public ReadyThreadRelationship ObserveReadyThread(
        DateTimeOffset timestampUtc,
        uint? wakerThreadId,
        uint? wakeeThreadId)
    {
        var relationship = new ReadyThreadRelationship(
            timestampUtc,
            ThreadIdentity.FromEtwThreadId(wakerThreadId),
            ThreadIdentity.FromEtwThreadId(wakeeThreadId));

        lock (_gate)
        {
            if (relationship.WakeeThread.Status == ThreadIdentityStatus.Resolved)
            {
                var state = new ThreadSchedulingState(
                    relationship.WakeeThread.ThreadId,
                    relationship.WakeeThread.Status,
                    ThreadStateLookup.GetName((int)EtwThreadState.Ready),
                    (int)EtwThreadState.Ready,
                    "ReadyThread",
                    timestampUtc,
                    -1,
                    null,
                    null,
                    relationship.WakerThread.ThreadId,
                    relationship.WakeeThread.ThreadId,
                    null,
                    null,
                    ThreadWaitCategory.Unknown,
                    false);
                StoreThreadState(state);
            }
        }

        return relationship;
    }

    public ThreadSchedulingState? ResolveThreadState(int threadId, DateTimeOffset observedAtUtc)
    {
        lock (_gate)
        {
            return _threadStates.TryGetValue(threadId, out var state) ? state : null;
        }
    }

    private void StoreThreadState(ThreadSchedulingState state)
    {
        if (!state.ThreadId.HasValue)
        {
            return;
        }

        _threadStates[state.ThreadId.Value] = state;
        while (_threadStates.Count > _maximumThreadStates)
        {
            var oldest = _threadStates.MinBy(pair => pair.Value.TimestampUtc);
            _threadStates.Remove(oldest.Key);
        }
    }

    private sealed record CpuRunningState(ThreadIdentity Thread, DateTimeOffset StartedAtUtc, int Priority);
}
