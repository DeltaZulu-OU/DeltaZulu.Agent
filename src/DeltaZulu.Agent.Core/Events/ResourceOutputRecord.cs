using System.Text.Json.Serialization;

namespace DeltaZulu.Agent.Core.Events;

/// <summary>
/// The structured NDJSON envelope emitted by Agent sinks.
/// Server-side normalization should consume this envelope and map event-native fields later.
/// </summary>
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
        var eventFields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = 1,
            ["profileId"] = profileId,
            ["profileVersion"] = profileVersion,
            ["ingestedAt"] = DateTimeOffset.UtcNow
        };

        IReadOnlyDictionary<string, object?>? enrichment = null;

        foreach (var field in projectedFields)
        {
            if (field.Key.Equals("_metadata", StringComparison.OrdinalIgnoreCase) && field.Value is IDictionary<string, object?> meta)
            {
                metadata = new Dictionary<string, object?>(meta, StringComparer.OrdinalIgnoreCase);
                metadata["profileId"] = profileId;
                metadata["profileVersion"] = profileVersion;
                continue;
            }

            if (field.Key.Equals("_metadata", StringComparison.OrdinalIgnoreCase) && field.Value is IDictionary<string, object> legacyMeta)
            {
                metadata = legacyMeta.ToDictionary(k => k.Key, v => (object?)v.Value, StringComparer.OrdinalIgnoreCase);
                metadata["profileId"] = profileId;
                metadata["profileVersion"] = profileVersion;
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
}