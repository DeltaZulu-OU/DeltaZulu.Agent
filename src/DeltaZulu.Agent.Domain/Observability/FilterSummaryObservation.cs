using DeltaZulu.Agent.Domain.Events;

namespace DeltaZulu.Agent.Domain.Observability;

public sealed record FilterSummaryObservation
{
    public const string RecordKind = "collector.filter.summary";

    public required CollectorObservationMetadata Metadata { get; init; }
    public required string SourceType { get; init; }
    public required string Channel { get; init; }
    public long ReadCount { get; init; }
    public long KeptAfterFilterCount { get; init; }
    public long DiscardedCount { get; init; }
    public long ForwardedCount { get; init; }

    public ResourceOutputRecord ToOutputRecord() => new()
    {
        Metadata = ObservationRecord.MetadataWithKind(Metadata, RecordKind),
        Event = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceType"] = SourceType,
            ["channel"] = Channel,
            ["readCount"] = ReadCount,
            ["keptAfterFilterCount"] = KeptAfterFilterCount,
            ["discardedCount"] = DiscardedCount,
            ["forwardedCount"] = ForwardedCount
        }
    };
}
