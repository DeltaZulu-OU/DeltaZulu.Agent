namespace DeltaZulu.Pipeline.Core.Etw;

public enum EtwSessionHealthStatus
{
    Healthy,
    Degraded,
    MissingSession,
    ProviderDisabled,
    Misconfigured,
    Unknown
}

public sealed record EtwForensicAlignmentMetadata(
    string? HostId,
    DateTimeOffset TimestampUtc,
    long? TimestampQpc,
    Guid ProviderGuid,
    string ProviderName,
    int EventId,
    int? Opcode,
    int? Version,
    int ProcessId,
    int ThreadId,
    Guid? ActivityId,
    Guid? RelatedActivityId,
    string EtwSessionName,
    string EtwProfileId,
    string EtwProfileVersion,
    string ParserName,
    string ParserVersion,
    string SchemaVersion,
    string? RawPayloadHash);

public sealed record EtwSessionHealthSnapshot(
    string EventType,
    string SessionName,
    string? ProfileId,
    string? ProfileVersion,
    string ProviderName,
    Guid? ProviderGuid,
    bool ExpectedEnabled,
    bool ObservedEnabled,
    string? ObservedLevel,
    ulong? ObservedKeywords,
    long EventsReceived,
    long EventsEmitted,
    long EventsDroppedByProfile,
    long EtwEventsLost,
    long ParserFailures,
    EtwSessionHealthStatus Status,
    DateTimeOffset ObservedAtUtc,
    string? CollectionMode = null,
    string? ParserName = null,
    string? ParserVersion = null,
    string? FilterVersion = null)
{
    public static EtwSessionHealthSnapshot FromMetrics(
        string sessionName,
        string? profileId,
        string? profileVersion,
        string providerName,
        Guid? providerGuid,
        bool expectedEnabled,
        bool observedEnabled,
        string? observedLevel,
        ulong? observedKeywords,
        EtwCollectorMetrics metrics,
        long eventsDroppedByProfile,
        DateTimeOffset observedAtUtc,
        string? collectionMode = null,
        string? parserName = null,
        string? parserVersion = null,
        string? filterVersion = null)
    {
        var status = DetermineStatus(expectedEnabled, observedEnabled, metrics.EtwEventsLostByEtw, metrics.ParserFailures);
        return new EtwSessionHealthSnapshot(
            "EtwSessionHealth",
            sessionName,
            profileId,
            profileVersion,
            providerName,
            providerGuid,
            expectedEnabled,
            observedEnabled,
            observedLevel,
            observedKeywords,
            metrics.EtwEventsReceived,
            metrics.EtwEventsEnqueued,
            eventsDroppedByProfile,
            metrics.EtwEventsLostByEtw,
            metrics.ParserFailures,
            status,
            observedAtUtc,
            collectionMode,
            parserName,
            parserVersion,
            filterVersion);
    }

    private static EtwSessionHealthStatus DetermineStatus(
        bool expectedEnabled,
        bool observedEnabled,
        long etwEventsLost,
        long parserFailures)
    {
        if (expectedEnabled && !observedEnabled)
        {
            return EtwSessionHealthStatus.ProviderDisabled;
        }

        if (etwEventsLost > 0 || parserFailures > 0)
        {
            return EtwSessionHealthStatus.Degraded;
        }

        return observedEnabled ? EtwSessionHealthStatus.Healthy : EtwSessionHealthStatus.Unknown;
    }
}
