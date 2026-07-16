using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Core.Observability;

public sealed record LocalCoverageStateObservation
{
    public const string RecordKind = "collector.coverage.local_state";

    public required CollectorObservationMetadata Metadata { get; init; }
    public required LogTelemetryKey LogKey { get; init; }
    public required string CmdbEntityId { get; init; }
    public required string EvaluationId { get; init; }
    public string? AuditPolicyCategory { get; init; }
    public string? AuditPolicySubcategory { get; init; }
    public string? AuditPolicySubcategoryGuid { get; init; }
    public bool? AuditSuccessEnabled { get; init; }
    public bool? AuditFailureEnabled { get; init; }
    public bool? SourceExists { get; init; }
    public bool? SourceReadable { get; init; }
    public long ReadErrorCount { get; init; }
    public long ActualReadCount { get; init; }
    public long KeptAfterFilterCount { get; init; }
    public long DiscardedCount { get; init; }
    public long OutputAcceptedCount { get; init; }
    public long OutputFailedCount { get; init; }

    public ResourceOutputRecord ToOutputRecord() => new() {
        Metadata = ObservationRecord.MetadataWithKind(Metadata, RecordKind),
        Event = new Dictionary<string, object?>(19, StringComparer.OrdinalIgnoreCase) {
            ["cmdbEntityId"] = CmdbEntityId,
            ["evaluationId"] = EvaluationId,
            ["sourceType"] = LogKey.SourceType,
            ["channel"] = LogKey.Channel,
            ["provider"] = LogKey.Provider,
            ["eventId"] = LogKey.EventId,
            ["auditPolicyCategory"] = AuditPolicyCategory,
            ["auditPolicySubcategory"] = AuditPolicySubcategory,
            ["auditPolicySubcategoryGuid"] = AuditPolicySubcategoryGuid,
            ["auditSuccessEnabled"] = AuditSuccessEnabled,
            ["auditFailureEnabled"] = AuditFailureEnabled,
            ["sourceExists"] = SourceExists,
            ["sourceReadable"] = SourceReadable,
            ["readErrorCount"] = ReadErrorCount,
            ["actualReadCount"] = ActualReadCount,
            ["keptAfterFilterCount"] = KeptAfterFilterCount,
            ["discardedCount"] = DiscardedCount,
            ["outputAcceptedCount"] = OutputAcceptedCount,
            ["outputFailedCount"] = OutputFailedCount
        }
    };
}
