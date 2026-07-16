namespace DeltaZulu.Pipeline.Enrichment.Windows;

public sealed record SidObservation
{
    public string EventType { get; init; } = "Sid";
    public string? TenantId { get; init; }
    public string? DeviceId { get; init; }
    public string? AgentId { get; init; }
    public string? BootId { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; }
    public required string Sid { get; init; }
    public string? ResolvedAccountName { get; init; }
    public string? ResolvedDomainName { get; init; }
    public string? ResolvedCanonicalName { get; init; }
    public string PrincipalType { get; init; } = "Unknown";
    public string SidScope { get; init; } = "Unknown";
    public string ResolutionSource { get; init; } = "Unknown";
    public string ResolutionStatus { get; init; } = "Unknown";
    public string ResolutionConfidence { get; init; } = "Unknown";
    public string? SourceRecordId { get; init; }
    public int? SourceEventId { get; init; }
    public string? SourceProvider { get; init; }
    public string LifecycleStatus { get; init; } = "Observed";
    public DateTimeOffset? DeletedAtUtc { get; init; }
    public DateTimeOffset CacheExpiresAtUtc { get; init; }
}

public sealed record SidResolutionResult(
    string Sid,
    string? AccountName,
    string? DomainName,
    string? CanonicalName,
    string PrincipalType,
    string SidScope,
    string ResolutionSource,
    string ResolutionStatus,
    string ResolutionConfidence,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset CacheExpiresAtUtc,
    string LifecycleStatus = "Observed",
    DateTimeOffset? DeletedAtUtc = null);

public interface IWindowsSidResolver
{
    SidResolutionResult Resolve(string sid, DateTimeOffset observedAtUtc, TimeSpan cacheTtl);
}
