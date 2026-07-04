namespace DeltaZulu.Pipeline.Enrichment.Windows;

public sealed record ProcessObservation
{
    public string EventType { get; init; } = "ProcessObservation";
    public string? TenantId { get; init; }
    public string? DeviceId { get; init; }
    public string? AgentId { get; init; }
    public string? BootId { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; }
    public required int ProcessId { get; init; }
    public string? ProcessImage { get; init; }
    public string? ProcessCommandLine { get; init; }
    public int? ParentProcessId { get; init; }
    public string? ParentProcessImage { get; init; }
    public string? ParentProcessCommandLine { get; init; }
    public string ObservationSource { get; init; } = "Unknown";
    public string ObservationStatus { get; init; } = "Observed";
    public string ObservationConfidence { get; init; } = "Unknown";
    public string? SourceRecordId { get; init; }
    public int? SourceEventId { get; init; }
    public string? SourceProvider { get; init; }
    public DateTimeOffset CacheExpiresAtUtc { get; init; }
}