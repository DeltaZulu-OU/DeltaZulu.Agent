using System.Globalization;
using DeltaZulu.Forward;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Core.Delivery;

public static class ResourceOutputRecordForwardMapper
{
    public static ForwardLogRecord ToForwardLogRecord(ResourceOutputRecord record)
    {
        var sourceType = GetString(record.Metadata, "sourceType") ?? "unknown";
        var sourceName = GetString(record.Metadata, "sourceName") ?? "unknown";
        var recordId = GetString(record.Event, "RecordId")
            ?? GetString(record.Event, "ID")
            ?? Guid.NewGuid().ToString("N");

        return new ForwardLogRecord {
            DeliveryId = Guid.NewGuid().ToString("N"),
            AgentId = GetString(record.Metadata, "collectorId") ?? GetString(record.Metadata, "hostname") ?? Environment.MachineName,
            SourceType = sourceType,
            SourceName = sourceName,
            ProfileId = GetString(record.Metadata, "profileId"),
            ProfileVersion = GetString(record.Metadata, "profileVersion"),
            Platform = GetString(record.Metadata, "platform"),
            Hostname = GetString(record.Metadata, "hostname"),
            RecordId = recordId,
            CreatedAt = DateTimeOffset.UtcNow,
            Fields = BuildFields(record)
        };
    }

    private static IReadOnlyDictionary<string, object?> BuildFields(ResourceOutputRecord record)
    {
        var fields = new Dictionary<string, object?>(record.Event, StringComparer.OrdinalIgnoreCase);
        if (record.Enrichment is not null)
        {
            foreach (var pair in record.Enrichment)
            {
                fields[pair.Key] = pair.Value;
            }
        }

        return fields;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch {
            string text when !string.IsNullOrWhiteSpace(text) => text,
            DateTimeOffset timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
            DateTime timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }
}
