using System.Text.Json.Serialization;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Core.Delivery;

public sealed record DeliveryRecord
{
    [JsonPropertyName("deliveryId")]
    public string DeliveryId { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("agentId")]
    public required string AgentId { get; init; }

    [JsonPropertyName("sourceId")]
    public required string SourceId { get; init; }

    [JsonPropertyName("profileId")]
    public string? ProfileId { get; init; }

    [JsonPropertyName("recordId")]
    public required string RecordId { get; init; }

    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("record")]
    public required ResourceOutputRecord Record { get; init; }

    public static DeliveryRecord FromResourceOutput(ResourceOutputRecord record)
    {
        var agentId = GetString(record.Metadata, "collectorId")
            ?? GetString(record.Metadata, "hostname")
            ?? Environment.MachineName;
        var sourceType = GetString(record.Metadata, "sourceType") ?? "unknown";
        var sourceName = GetString(record.Metadata, "sourceName") ?? "unknown";
        var profileId = GetString(record.Metadata, "profileId");
        var recordId = GetString(record.Event, "RecordId")
            ?? GetString(record.Event, "ID")
            ?? Guid.NewGuid().ToString("N");

        return new DeliveryRecord {
            AgentId = agentId,
            SourceId = $"{sourceType}:{sourceName}",
            ProfileId = profileId,
            RecordId = recordId,
            CreatedAt = DateTimeOffset.UtcNow,
            Record = record
        };
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch {
            string text when !string.IsNullOrWhiteSpace(text) => text,
            DateTimeOffset timestamp => timestamp.ToString("O"),
            DateTime timestamp => timestamp.ToString("O"),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}