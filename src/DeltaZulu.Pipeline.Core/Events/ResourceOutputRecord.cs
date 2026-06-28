using System.Text.Json.Serialization;

namespace DeltaZulu.Pipeline.Core.Events;

public sealed record ResourceOutputRecord
{
    [JsonPropertyName("_metadata")]
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();

    [JsonPropertyName("event")]
    public IReadOnlyDictionary<string, object?> Event { get; init; } = new Dictionary<string, object?>();

    [JsonPropertyName("enrichment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, object?>? Enrichment { get; init; }

    public static ResourceOutputRecord FromSource(SourceEvent source, string? profileId = null, string? profileVersion = null)
    {
        var metadata = new Dictionary<string, object?>(source.Metadata.ToDictionary(), StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            metadata["profileId"] = profileId;
        }

        if (!string.IsNullOrWhiteSpace(profileVersion))
        {
            metadata["profileVersion"] = profileVersion;
        }

        return new ResourceOutputRecord
        {
            Metadata = metadata,
            Event = new Dictionary<string, object?>(source.Fields, StringComparer.OrdinalIgnoreCase)
        };
    }

    public static ResourceOutputRecord FromKqlProjection(
        IReadOnlyDictionary<string, object?> projectedFields,
        string profileId,
        string? profileVersion)
    {
        return FromKqlProjection(projectedFields, profileId, profileVersion, sourceMetadata: null);
    }

    public static ResourceOutputRecord FromKqlProjection(
        IReadOnlyDictionary<string, object?> projectedFields,
        string profileId,
        string? profileVersion,
        ResourceMetadata? sourceMetadata)
    {
        var eventFields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = 1,
            ["profileId"] = profileId,
            ["profileVersion"] = profileVersion,
            ["ingestedAt"] = DateTimeOffset.UtcNow
        };

        if (sourceMetadata is not null)
        {
            metadata["collectorId"] = sourceMetadata.CollectorId;
            metadata["sourceType"] = sourceMetadata.SourceType;
            metadata["sourceName"] = sourceMetadata.SourceName;
            metadata["platform"] = sourceMetadata.Platform;
            metadata["hostname"] = sourceMetadata.Hostname;
            metadata["parserName"] = sourceMetadata.ParserName;
            metadata["parserVersion"] = sourceMetadata.ParserVersion;
            metadata["rawPreserved"] = sourceMetadata.RawPreserved;
        }

        IReadOnlyDictionary<string, object?>? enrichment = null;

        foreach (var field in projectedFields)
        {
            if (field.Key.Equals("_metadata", StringComparison.OrdinalIgnoreCase) && field.Value is IDictionary<string, object?> meta)
            {
                var projected = new Dictionary<string, object?>(meta, StringComparer.OrdinalIgnoreCase);
                projected["profileId"] = profileId;
                projected["profileVersion"] = profileVersion;
                PreserveDeliveryIdentity(metadata, projected);
                metadata = projected;
                continue;
            }

            if (field.Key.Equals("_metadata", StringComparison.OrdinalIgnoreCase) && field.Value is IDictionary<string, object> legacyMeta)
            {
                var projected = legacyMeta.ToDictionary(k => k.Key, v => (object?)v.Value, StringComparer.OrdinalIgnoreCase);
                projected["profileId"] = profileId;
                projected["profileVersion"] = profileVersion;
                PreserveDeliveryIdentity(metadata, projected);
                metadata = projected;
                continue;
            }

            if (field.Key.Equals("enrichment", StringComparison.OrdinalIgnoreCase))
            {
                enrichment = DictionaryCoercion.CoerceToNullableDictionary(field.Value);
                continue;
            }

            eventFields[field.Key] = field.Value;
        }

        return new ResourceOutputRecord
        {
            Metadata = metadata,
            Event = eventFields,
            Enrichment = enrichment
        };
    }

    private static readonly string[] DeliveryIdentityFields =
    [
        "collectorId", "sourceType", "sourceName", "platform", "hostname"
    ];

    private static void PreserveDeliveryIdentity(
        IDictionary<string, object?> source,
        IDictionary<string, object?> target)
    {
        foreach (var key in DeliveryIdentityFields)
        {
            if (!target.ContainsKey(key) && source.TryGetValue(key, out var value) && value is not null)
            {
                target[key] = value;
            }
        }
    }
}
