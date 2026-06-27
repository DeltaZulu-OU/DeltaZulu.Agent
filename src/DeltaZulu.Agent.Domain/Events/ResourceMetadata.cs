namespace DeltaZulu.Agent.Domain.Events;

public sealed record ResourceMetadata
{
    public int SchemaVersion { get; init; } = 1;
    public string CollectorId { get; init; } = Environment.MachineName;
    public string? ProfileId { get; init; }
    public string? ProfileVersion { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public string Hostname { get; init; } = Environment.MachineName;
    public DateTimeOffset IngestedAt { get; init; } = DateTimeOffset.UtcNow;
    public string ParserName { get; init; } = string.Empty;
    public string ParserVersion { get; init; } = "1.0.0";
    public bool RawPreserved { get; init; }
    public IReadOnlyDictionary<string, object?> Properties { get; init; } = new Dictionary<string, object?>();

    public IDictionary<string, object?> ToDictionary()
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = SchemaVersion,
            ["collectorId"] = CollectorId,
            ["profileId"] = ProfileId,
            ["profileVersion"] = ProfileVersion,
            ["sourceType"] = SourceType,
            ["sourceName"] = SourceName,
            ["platform"] = Platform,
            ["hostname"] = Hostname,
            ["ingestedAt"] = IngestedAt,
            ["parserName"] = ParserName,
            ["parserVersion"] = ParserVersion,
            ["rawPreserved"] = RawPreserved
        };

        foreach (var property in Properties)
        {
            dict[property.Key] = property.Value;
        }

        return dict;
    }
}
