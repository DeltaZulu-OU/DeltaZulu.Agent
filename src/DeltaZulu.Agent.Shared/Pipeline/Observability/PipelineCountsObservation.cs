using DeltaZulu.Agent.Shared.Pipeline.Events;

namespace DeltaZulu.Agent.Shared.Pipeline.Observability;

public sealed record PipelineCountsObservation
{
    public const string RecordKind = "collector.pipeline.counts";

    public required LogTelemetryKey LogKey { get; init; }
    public required CollectorObservationMetadata Metadata { get; init; }
    public long ReadCount { get; init; }
    public long KeptAfterFilterCount { get; init; }
    public long DiscardedCount { get; init; }
    public long ForwardedCount { get; init; }
    public long ForwardFailedCount { get; init; }

    public ResourceOutputRecord ToOutputRecord() => new()
    {
        Metadata = ObservationRecord.MetadataWithKind(Metadata, RecordKind),
        Event = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceType"] = LogKey.SourceType,
            ["channel"] = LogKey.Channel,
            ["provider"] = LogKey.Provider,
            ["eventId"] = LogKey.EventId,
            ["readCount"] = ReadCount,
            ["keptAfterFilterCount"] = KeptAfterFilterCount,
            ["discardedCount"] = DiscardedCount,
            ["forwardedCount"] = ForwardedCount,
            ["forwardFailedCount"] = ForwardFailedCount
        }
    };
}
