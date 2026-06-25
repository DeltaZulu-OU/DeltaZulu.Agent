namespace DeltaZulu.Buffer.Chunks;

public enum ChunkState
{
    Open,
    Sealing,
    Sealed,
    Dispatchable,
    Dispatching,
    Delivered,
    Deleted,
    RetryScheduled,
    DeadLettered,
    Rejected,
    Corrupt,
    Quarantined
}