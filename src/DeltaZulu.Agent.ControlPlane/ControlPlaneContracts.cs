namespace DeltaZulu.Agent.ControlPlane;

// Wire contracts for the DeltaZulu.Platform agent control plane, `/api/agent/v1`
// (enroll, heartbeat, policy pull/ack, command result). Shapes must stay in lockstep
// with the server-side contracts in DeltaZulu.Platform; this is the Agent-side source
// of truth, not an independently maintained copy.

public sealed record EnrollRequest(
    string BootstrapToken,
    string Hostname,
    string Platform,
    string? AgentVersion,
    IReadOnlyList<string>? Tags,
    string? PreviousAgentSecret = null);

public sealed record EnrollResponse(
    string AgentId,
    string TenantId,
    string AgentSecret,
    int HeartbeatIntervalSeconds);

public sealed record HeartbeatRequest(
    string? AgentVersion,
    string? AppliedBundleId,
    string? AppliedBundleHash,
    string? ReportedStatus,
    double BufferPressure,
    long QueueDepth,
    long DroppedCount,
    long ForwardFailedCount,
    IReadOnlyList<SourceHealthEntry>? Sources = null);

public sealed record SourceHealthEntry(
    string SourceType,
    string Channel,
    bool IsEnabled,
    bool CanRead,
    DateTimeOffset? LastReadAt,
    long ReadErrorCount,
    string? LastError,
    long ReadCount,
    long KeptAfterFilterCount,
    long DiscardedCount,
    long ForwardedCount,
    long ForwardFailedCount,
    string? SourceInstanceId = null,
    string? ResourceFamily = null,
    string? Provider = null,
    string? ProfileId = null,
    string? ProfileVersionId = null);

public sealed record HeartbeatResponse(
    string? DesiredBundleId,
    string? DesiredBundleHash,
    bool PolicyChanged,
    IReadOnlyList<CommandEntry>? Commands);

public sealed record CommandEntry(
    string CommandId,
    string Type,
    int TimeoutSeconds,
    DateTimeOffset RequestedAt);

public sealed record CommandResultRequest(
    bool Succeeded,
    string? ResultJson,
    string? Error);

public sealed record BundleResponse(
    string BundleId,
    string ContentHash,
    DateTimeOffset CreatedAt,
    System.Text.Json.JsonElement Document);

public sealed record AckRequest(
    string BundleId,
    string Status,
    string? Error);
