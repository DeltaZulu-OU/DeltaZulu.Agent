using System.Text.Json.Serialization;

namespace DeltaZulu.Pipeline.Core.Events;

public sealed record ResourceOutputRecord
{
    public const string QueryResultShapeMetadataKey = "queryResultShape";
    public const string DerivedFromSourceMetadataKey = "derivedFromSource";
    public const string QueryDerivedFieldsMetadataKey = "queryDerivedFields";
    public const string SourceShapedQueryResult = "source-shaped";
    public const string DerivedProjectedQueryResult = "derived/projected";

    [JsonPropertyName("_metadata")]
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();

    [JsonPropertyName("event")]
    public IReadOnlyDictionary<string, object?> Event { get; init; } = new Dictionary<string, object?>();

    [JsonPropertyName("enrichment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, object?>? Enrichment { get; init; }

    public static ResourceOutputRecord FromSource(SourceEvent source, string? profileId = null, string? profileVersion = null)
    {
        var metadata = source.Metadata.ToDictionary();
        if (!string.IsNullOrWhiteSpace(profileId) || !string.IsNullOrWhiteSpace(profileVersion))
        {
            metadata.EnsureCapacity(metadata.Count + 2);
        }
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            metadata["profileId"] = profileId;
        }

        if (!string.IsNullOrWhiteSpace(profileVersion))
        {
            metadata["profileVersion"] = profileVersion;
        }

        return new ResourceOutputRecord {
            Metadata = metadata,
            Event = DictionaryCoercion.ToObjectDictionary(source.Fields)
        };
    }

    public static ResourceOutputRecord FromKqlProjection(
        IReadOnlyDictionary<string, object?> projectedFields,
        string profileId,
        string? profileVersion) => FromKqlProjection(projectedFields, profileId, profileVersion, sourceMetadata: null);

    public static ResourceOutputRecord FromKqlProjection(
        IReadOnlyDictionary<string, object?> projectedFields,
        string profileId,
        string? profileVersion,
        ResourceMetadata? sourceMetadata,
        SourceEvent? sourceEvent = null)
    {
        var eventFields = new Dictionary<string, object?>(projectedFields.Count, StringComparer.OrdinalIgnoreCase);
        var metadata = new Dictionary<string, object?>(12, StringComparer.OrdinalIgnoreCase) {
            ["schemaVersion"] = 1,
            ["profileId"] = profileId,
            ["profileVersion"] = profileVersion,
            ["ingestedAt"] = DateTimeOffset.UtcNow,
            ["eventUid"] = Guid.NewGuid().ToString("N"),
            ["resolverVersion"] = string.Empty
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
            metadata["resolverVersion"] = sourceMetadata.ResolverVersion;
            metadata["eventUid"] = sourceMetadata.EventUid;
            metadata["originalTimestamp"] = sourceMetadata.OriginalTimestamp;
            metadata["rawPreserved"] = sourceMetadata.RawPreserved;
        }

        IReadOnlyDictionary<string, object?>? enrichment = null;

        foreach (var field in projectedFields)
        {
            if (field.Key.Equals("_metadata", StringComparison.OrdinalIgnoreCase) && field.Value is IDictionary<string, object?> meta)
            {
                var projected = DictionaryCoercion.ToObjectDictionary(meta);
                projected.EnsureCapacity(projected.Count + 2);
                projected["profileId"] = profileId;
                projected["profileVersion"] = profileVersion;
                PreserveDeliveryIdentity(metadata, projected);
                metadata = projected;
                continue;
            }

            if (field.Key.Equals("_metadata", StringComparison.OrdinalIgnoreCase) && field.Value is IDictionary<string, object> legacyMeta)
            {
                var projected = new Dictionary<string, object?>(legacyMeta.Count + 2, StringComparer.OrdinalIgnoreCase);
                foreach (var item in legacyMeta)
                {
                    projected[item.Key] = item.Value;
                }
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

        ApplyQueryResultProvenance(metadata, eventFields, sourceEvent);

        return new ResourceOutputRecord {
            Metadata = metadata,
            Event = eventFields,
            Enrichment = enrichment
        };
    }

    private static void ApplyQueryResultProvenance(
        IDictionary<string, object?> metadata,
        IReadOnlyDictionary<string, object?> eventFields,
        SourceEvent? sourceEvent)
    {
        if (sourceEvent is null)
        {
            // A result without a source row (for example, a future aggregate) is
            // necessarily a query-derived result rather than a forwarding record.
            metadata[QueryResultShapeMetadataKey] = DerivedProjectedQueryResult;
            metadata[DerivedFromSourceMetadataKey] = true;
            metadata[QueryDerivedFieldsMetadataKey] = eventFields.Keys.ToArray();
            return;
        }

        var sourceFields = sourceEvent.ToKqlRow();
        sourceFields.Remove("_metadata");
        var derivedFields = eventFields.Keys
            .Where(key => !sourceFields.ContainsKey(key))
            .ToArray();
        var sourceFieldsWereDropped = sourceFields.Keys.Any(key => !eventFields.ContainsKey(key));
        var derived = derivedFields.Length > 0 || sourceFieldsWereDropped;

        metadata[QueryResultShapeMetadataKey] = derived ? DerivedProjectedQueryResult : SourceShapedQueryResult;
        metadata[DerivedFromSourceMetadataKey] = derived;
        metadata[QueryDerivedFieldsMetadataKey] = derivedFields;
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
