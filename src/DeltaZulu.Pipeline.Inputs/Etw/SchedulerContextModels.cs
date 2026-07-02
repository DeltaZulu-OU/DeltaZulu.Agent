namespace DeltaZulu.Pipeline.Inputs.Etw;

public enum ThreadWaitCategory
{
    IoWait,
    ExecutionDelay,
    Synchronization,
    Memory,
    Unknown
}

public enum ThreadIdentityStatus
{
    Resolved,
    Missing,
    Anonymized
}

public sealed record ThreadIdentity(int? ThreadId, ThreadIdentityStatus Status)
{
    public const uint AnonymizedThreadId = uint.MaxValue;

    public static ThreadIdentity FromEtwThreadId(uint? threadId) => threadId switch {
        null => new ThreadIdentity(null, ThreadIdentityStatus.Missing),
        AnonymizedThreadId => new ThreadIdentity(null, ThreadIdentityStatus.Anonymized),
        _ => new ThreadIdentity(checked((int)threadId.Value), ThreadIdentityStatus.Resolved)
    };
}

public sealed record ThreadWaitClassification(
    int? ThreadWaitReasonCode,
    string? ThreadWaitReason,
    ThreadWaitCategory ThreadWaitCategory,
    bool ThreadWasInIoWait,
    string ThreadWaitReasonSource);

public sealed record ThreadSchedulingState(
    int? ThreadId,
    ThreadIdentityStatus ThreadIdentityStatus,
    string? ThreadState,
    int? ThreadStateCode,
    string ThreadStateSource,
    DateTimeOffset TimestampUtc,
    int CpuId,
    int? PreviousThreadId,
    int? NextThreadId,
    int? WakerThreadId,
    int? WakeeThreadId,
    int? ThreadWaitReasonCode,
    string? ThreadWaitReason,
    ThreadWaitCategory ThreadWaitCategory,
    bool ThreadWasInIoWait)
{
    public long AgeMs(DateTimeOffset observedAtUtc) =>
        Math.Max(0, (long)(observedAtUtc - TimestampUtc).TotalMilliseconds);
}

public sealed record ReadyThreadRelationship(
    DateTimeOffset TimestampUtc,
    ThreadIdentity WakerThread,
    ThreadIdentity WakeeThread);
