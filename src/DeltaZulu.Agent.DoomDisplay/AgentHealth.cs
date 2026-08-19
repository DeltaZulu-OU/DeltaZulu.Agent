using DeltaZulu.Forward;

namespace DeltaZulu.Agent.DoomDisplay;

/// <summary>
/// One reading of the agent daemon's published health state. Unavailability is a first-class
/// reading rather than an exception: a daemon that stops publishing must be visible on the
/// display, not silently frozen at its last good values.
/// </summary>
public sealed record AgentHealthSnapshot
{
    /// <summary>The record kind the agent daemon publishes forwarder health under.</summary>
    public const string RecordKind = "collector.forwarder.health";

    public required bool Available { get; init; }

    /// <summary>Why the reading is unavailable. Empty when <see cref="Available" /> is true.</summary>
    public string UnavailableReason { get; init; } = string.Empty;

    /// <summary>The instant the daemon observed this state, not the instant it was read.</summary>
    public DateTimeOffset? ObservedAtUtc { get; init; }

    public string? AgentId { get; init; }
    public string? HostId { get; init; }
    public string? BufferState { get; init; }
    public long? DiskBytesUsed { get; init; }
    public long? DiskBytesLimit { get; init; }
    public long? MemoryBytesUsed { get; init; }
    public long? SealedChunkCount { get; init; }
    public double? OldestChunkAgeMilliseconds { get; init; }
    public long? RecordsAcceptedTotal { get; init; }
    public long? RecordsRejectedTotal { get; init; }
    public long? RecordsDroppedTotal { get; init; }
    public long? ChunksCompletedTotal { get; init; }
    public long? ChunksDeadLetteredTotal { get; init; }
    public DateTimeOffset? LastForwarderActivityUtc { get; init; }
    public bool? TransportIsRunning { get; init; }
    public long? TransportSendAttemptsTotal { get; init; }
    public long? TransportSendSuccessesTotal { get; init; }
    public long? TransportTransientFailuresTotal { get; init; }
    public long? TransportPermanentFailuresTotal { get; init; }

    public static AgentHealthSnapshot Unavailable(string reason) =>
        new() { Available = false, UnavailableReason = reason };
}

/// <summary>
/// Converts an <see cref="AgentHealthSnapshot" /> to and from a DeltaZulu.Forward
/// <see cref="ForwardLogBatch" />. Health travels the typed telemetry contract, not the opaque
/// display contract: it is a <see cref="ForwardFrameType.TypedBatch" /> frame carrying the same
/// <c>collector.forwarder.health</c> field names the agent's own pipeline publishes, so a real
/// collector could route it to a SIEM unchanged.
/// </summary>
public static class AgentHealthCodec
{
    public const string SourceType = "agent-health";
    public const string SourceName = "forwarder-health";

    public static ForwardLogBatch ToBatch(AgentHealthSnapshot snapshot, string agentId, string hostname)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var fields = new Dictionary<string, object?>(24, StringComparer.OrdinalIgnoreCase) {
            ["available"] = snapshot.Available,
            ["unavailableReason"] = snapshot.UnavailableReason,
            ["observedAtUtc"] = snapshot.ObservedAtUtc,
            ["agentId"] = snapshot.AgentId,
            ["hostId"] = snapshot.HostId,
            ["bufferState"] = snapshot.BufferState,
            ["diskBytesUsed"] = snapshot.DiskBytesUsed,
            ["diskBytesLimit"] = snapshot.DiskBytesLimit,
            ["memoryBytesUsed"] = snapshot.MemoryBytesUsed,
            ["sealedChunkCount"] = snapshot.SealedChunkCount,
            ["oldestChunkAgeMs"] = snapshot.OldestChunkAgeMilliseconds,
            ["recordsAcceptedTotal"] = snapshot.RecordsAcceptedTotal,
            ["recordsRejectedTotal"] = snapshot.RecordsRejectedTotal,
            ["recordsDroppedTotal"] = snapshot.RecordsDroppedTotal,
            ["chunksCompletedTotal"] = snapshot.ChunksCompletedTotal,
            ["chunksDeadLetteredTotal"] = snapshot.ChunksDeadLetteredTotal,
            ["lastForwarderActivityUtc"] = snapshot.LastForwarderActivityUtc,
            ["transportIsRunning"] = snapshot.TransportIsRunning,
            ["transportSendAttemptsTotal"] = snapshot.TransportSendAttemptsTotal,
            ["transportSendSuccessesTotal"] = snapshot.TransportSendSuccessesTotal,
            ["transportTransientFailuresTotal"] = snapshot.TransportTransientFailuresTotal,
            ["transportPermanentFailuresTotal"] = snapshot.TransportPermanentFailuresTotal
        };

        var batchId = Guid.NewGuid();
        return new ForwardLogBatch {
            BatchId = batchId,
            Records = [
                new ForwardLogRecord {
                    DeliveryId = batchId.ToString(),
                    RecordId = Guid.NewGuid().ToString(),
                    AgentId = agentId,
                    SourceType = SourceType,
                    SourceName = SourceName,
                    Hostname = hostname,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Fields = fields
                }
            ]
        };
    }

    /// <summary>
    /// Reads the first health record out of a decoded batch. A batch that carries no record, or
    /// whose record is not agent health, is a contract violation and is rejected by the caller.
    /// </summary>
    public static AgentHealthSnapshot FromBatch(ForwardLogBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Records.Count == 0)
        {
            throw new InvalidDataException("Agent-health batch carries no records.");
        }

        var record = batch.Records[0];
        if (!string.Equals(record.SourceType, SourceType, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Expected a '{SourceType}' record, received '{record.SourceType}'.");
        }

        var fields = record.Fields;
        return new AgentHealthSnapshot {
            Available = Bool(fields, "available") ?? false,
            UnavailableReason = Text(fields, "unavailableReason") ?? string.Empty,
            ObservedAtUtc = Timestamp(fields, "observedAtUtc"),
            AgentId = Text(fields, "agentId"),
            HostId = Text(fields, "hostId"),
            BufferState = Text(fields, "bufferState"),
            DiskBytesUsed = Integer(fields, "diskBytesUsed"),
            DiskBytesLimit = Integer(fields, "diskBytesLimit"),
            MemoryBytesUsed = Integer(fields, "memoryBytesUsed"),
            SealedChunkCount = Integer(fields, "sealedChunkCount"),
            OldestChunkAgeMilliseconds = Real(fields, "oldestChunkAgeMs"),
            RecordsAcceptedTotal = Integer(fields, "recordsAcceptedTotal"),
            RecordsRejectedTotal = Integer(fields, "recordsRejectedTotal"),
            RecordsDroppedTotal = Integer(fields, "recordsDroppedTotal"),
            ChunksCompletedTotal = Integer(fields, "chunksCompletedTotal"),
            ChunksDeadLetteredTotal = Integer(fields, "chunksDeadLetteredTotal"),
            LastForwarderActivityUtc = Timestamp(fields, "lastForwarderActivityUtc"),
            TransportIsRunning = Bool(fields, "transportIsRunning"),
            TransportSendAttemptsTotal = Integer(fields, "transportSendAttemptsTotal"),
            TransportSendSuccessesTotal = Integer(fields, "transportSendSuccessesTotal"),
            TransportTransientFailuresTotal = Integer(fields, "transportTransientFailuresTotal"),
            TransportPermanentFailuresTotal = Integer(fields, "transportPermanentFailuresTotal")
        };
    }

    private static object? Value(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : null;

    private static string? Text(IReadOnlyDictionary<string, object?> fields, string key) =>
        Value(fields, key)?.ToString();

    private static bool? Bool(IReadOnlyDictionary<string, object?> fields, string key) => Value(fields, key) switch {
        bool value => value,
        long value => value != 0,
        string value when bool.TryParse(value, out var parsed) => parsed,
        _ => null
    };

    private static long? Integer(IReadOnlyDictionary<string, object?> fields, string key) => Value(fields, key) switch {
        long value => value,
        int value => value,
        double value => (long)value,
        string value when long.TryParse(value, out var parsed) => parsed,
        _ => null
    };

    private static double? Real(IReadOnlyDictionary<string, object?> fields, string key) => Value(fields, key) switch {
        double value => value,
        long value => value,
        int value => value,
        string value when double.TryParse(value, out var parsed) => parsed,
        _ => null
    };

    private static DateTimeOffset? Timestamp(IReadOnlyDictionary<string, object?> fields, string key) => Value(fields, key) switch {
        DateTimeOffset value => value,
        DateTime value => new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero),
        string value when DateTimeOffset.TryParse(value, out var parsed) => parsed,
        _ => null
    };
}
