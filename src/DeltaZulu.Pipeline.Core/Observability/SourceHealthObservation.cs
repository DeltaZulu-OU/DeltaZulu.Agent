using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Core.Observability;

public sealed record SourceHealthObservation
{
    public const string RecordKind = "collector.source.health";

    public required CollectorObservationMetadata Metadata { get; init; }
    public required string SourceType { get; init; }
    public required string Channel { get; init; }
    public bool IsEnabled { get; init; }
    public bool CanRead { get; init; }
    public DateTimeOffset? LastReadAt { get; init; }
    public long ReadErrorCount { get; init; }
    public string? LastError { get; init; }

    public ResourceOutputRecord ToOutputRecord() => new()
    {
        Metadata = ObservationRecord.MetadataWithKind(Metadata, RecordKind),
        Event = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceType"] = SourceType,
            ["channel"] = Channel,
            ["isEnabled"] = IsEnabled,
            ["canRead"] = CanRead,
            ["lastReadAt"] = LastReadAt,
            ["readErrorCount"] = ReadErrorCount,
            ["lastError"] = LastError
        }
    };
}
