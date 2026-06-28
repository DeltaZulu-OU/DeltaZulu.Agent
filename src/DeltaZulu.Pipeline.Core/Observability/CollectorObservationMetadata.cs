namespace DeltaZulu.Pipeline.Core.Observability;

public sealed record CollectorObservationMetadata
{
    public string AgentId { get; init; } = Environment.MachineName;
    public string HostId { get; init; } = Environment.MachineName;
    public string? ProfileId { get; init; }
    public string? FilterId { get; init; }
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? WindowStart { get; init; }
    public DateTimeOffset? WindowEnd { get; init; }

    public IReadOnlyDictionary<string, object?> ToDictionary()
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["agentId"] = AgentId,
            ["hostId"] = HostId,
            ["profileId"] = ProfileId,
            ["observedAt"] = ObservedAt
        };

        if (!string.IsNullOrWhiteSpace(FilterId)) values["filterId"] = FilterId;
        if (WindowStart is not null) values["windowStart"] = WindowStart;
        if (WindowEnd is not null) values["windowEnd"] = WindowEnd;
        return values;
    }
}
