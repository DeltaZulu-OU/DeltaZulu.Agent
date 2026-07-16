namespace DeltaZulu.Pipeline.Core.Windows;

/// <summary>Explicit start-position policy for live Windows Event Log collection.</summary>
public enum EventLogStartPosition
{
    /// <summary>Collect only records newer than the latest record present at startup.</summary>
    FromNow,

    /// <summary>Collect from the oldest available record in the channel.</summary>
    FromOldest,

    /// <summary>Collect records after a configured EventRecordID.</summary>
    FromRecordId,

    /// <summary>Collect records newer than a configured time window.</summary>
    Lookback,

    /// <summary>Resume from a persisted bookmark if present; otherwise use the configured fallback.</summary>
    Bookmark
}

/// <summary>What a resolved start position instructs the reader to do for its initial read.</summary>
public enum ResolvedStartKind
{
    /// <summary>Seek to the current tail; collect only newer records.</summary>
    Newest,

    /// <summary>Start from the oldest available record.</summary>
    Oldest,

    /// <summary>Start after a specific EventRecordID.</summary>
    AfterRecordId,

    /// <summary>Start from records newer than a time window.</summary>
    Lookback
}

/// <summary>Concrete initial-read instruction produced by <see cref="EventLogStartPositionResolver"/>.</summary>
public readonly record struct ResolvedStartPosition(ResolvedStartKind Kind, long RecordId = 0, TimeSpan Lookback = default)
{
    public static ResolvedStartPosition Newest { get; } = new(ResolvedStartKind.Newest);
    public static ResolvedStartPosition Oldest { get; } = new(ResolvedStartKind.Oldest);
    public static ResolvedStartPosition AfterRecord(long recordId) => new(ResolvedStartKind.AfterRecordId, recordId);
    public static ResolvedStartPosition Within(TimeSpan lookback) => new(ResolvedStartKind.Lookback, Lookback: lookback);
}
