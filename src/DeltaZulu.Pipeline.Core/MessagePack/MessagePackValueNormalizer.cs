using System.Collections;
using System.Globalization;
using System.Text.Json;
using DeltaZulu.Pipeline.Core.Delivery;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Core.MessagePack;

internal static class MessagePackValueNormalizer
{
    public static T Normalize<T>(T value) => value switch {
        DeliveryBatch batch => (T)(object)NormalizeBatch(batch),
        DeliveryRecord record => (T)(object)NormalizeRecord(record),
        ResourceOutputRecord record => (T)(object)NormalizeResourceOutput(record),
        _ => value
    };

    private static DeliveryBatch NormalizeBatch(DeliveryBatch batch)
    {
        List<DeliveryRecord>? normalizedRecords = null;
        for (var index = 0; index < batch.Records.Count; index++)
        {
            var record = batch.Records[index];
            var normalized = NormalizeRecord(record);
            if (normalizedRecords is not null)
            {
                normalizedRecords.Add(normalized);
            }
            else if (!ReferenceEquals(normalized, record))
            {
                normalizedRecords = new List<DeliveryRecord>(batch.Records.Count);
                for (var copyIndex = 0; copyIndex < index; copyIndex++)
                {
                    normalizedRecords.Add(batch.Records[copyIndex]);
                }

                normalizedRecords.Add(normalized);
            }
        }

        return normalizedRecords is null ? batch : batch with { Records = normalizedRecords };
    }

    private static DeliveryRecord NormalizeRecord(DeliveryRecord record)
    {
        var normalizedRecord = NormalizeResourceOutput(record.Record);
        return ReferenceEquals(normalizedRecord, record.Record) ? record : record with { Record = normalizedRecord };
    }

    private static ResourceOutputRecord NormalizeResourceOutput(ResourceOutputRecord record)
    {
        var metadata = NormalizeStringDictionary(record.Metadata);
        var eventFields = NormalizeStringDictionary(record.Event);
        var enrichment = record.Enrichment is null ? null : NormalizeStringDictionary(record.Enrichment);

        return ReferenceEquals(metadata, record.Metadata) &&
            ReferenceEquals(eventFields, record.Event) &&
            ReferenceEquals(enrichment, record.Enrichment)
            ? record
            : record with { Metadata = metadata, Event = eventFields, Enrichment = enrichment };
    }

    private static IReadOnlyDictionary<string, object?> NormalizeStringDictionary(IReadOnlyDictionary<string, object?> dictionary)
    {
        Dictionary<string, object?>? normalized = null;
        foreach (var pair in dictionary)
        {
            var normalizedValue = NormalizeValue(pair.Value);
            if (normalized is not null)
            {
                normalized[pair.Key] = normalizedValue;
            }
            else if (!ReferenceEquals(normalizedValue, pair.Value))
            {
                normalized = new Dictionary<string, object?>(dictionary.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var copy in dictionary)
                {
                    if (StringComparer.OrdinalIgnoreCase.Equals(copy.Key, pair.Key))
                    {
                        break;
                    }

                    normalized[copy.Key] = copy.Value;
                }

                normalized[pair.Key] = normalizedValue;
            }
        }

        return normalized ?? dictionary;
    }

    private static object? NormalizeValue(object? value) => value switch {
        null => null,
        string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double => value,
        decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
        char charValue => charValue.ToString(),
        DateTimeOffset timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
        DateTime timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString("D"),
        Enum enumValue => enumValue.ToString(),
        JsonElement element => NormalizeJsonElement(element),
        IReadOnlyDictionary<string, object?> dictionary => NormalizeStringDictionary(dictionary),
        IDictionary<string, object?> dictionary => NormalizeMutableStringDictionary(dictionary),
        IDictionary dictionary => NormalizeObjectDictionary(dictionary),
        byte[] bytes => bytes,
        IEnumerable enumerable when value is not string => NormalizeEnumerable(enumerable),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private static object? NormalizeJsonElement(JsonElement element) => element.ValueKind switch {
        JsonValueKind.Object => NormalizeJsonObject(element),
        JsonValueKind.Array => NormalizeJsonArray(element),
        JsonValueKind.String => element.TryGetDateTimeOffset(out var timestamp) ? timestamp.ToString("O", CultureInfo.InvariantCulture) : element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.TryGetDouble(out var number) ? number : element.GetRawText(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.GetRawText()
    };

    private static IReadOnlyDictionary<string, object?> NormalizeJsonObject(JsonElement element)
    {
        var properties = element.EnumerateObject();
        var normalized = new Dictionary<string, object?>(properties.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            normalized[property.Name] = NormalizeJsonElement(property.Value);
        }

        return normalized;
    }

    private static IReadOnlyList<object?> NormalizeJsonArray(JsonElement element)
    {
        var items = element.EnumerateArray();
        var normalized = new List<object?>(items.Count());
        foreach (var item in element.EnumerateArray())
        {
            normalized.Add(NormalizeJsonElement(item));
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, object?> NormalizeMutableStringDictionary(IDictionary<string, object?> dictionary)
    {
        Dictionary<string, object?>? normalized = null;
        foreach (var pair in dictionary)
        {
            var normalizedValue = NormalizeValue(pair.Value);
            if (normalized is not null)
            {
                normalized[pair.Key] = normalizedValue;
            }
            else if (!ReferenceEquals(normalizedValue, pair.Value))
            {
                normalized = new Dictionary<string, object?>(dictionary.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var copy in dictionary)
                {
                    if (StringComparer.OrdinalIgnoreCase.Equals(copy.Key, pair.Key))
                    {
                        break;
                    }

                    normalized[copy.Key] = copy.Value;
                }

                normalized[pair.Key] = normalizedValue;
            }
        }

        return normalized ?? (dictionary as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<string, object?> NormalizeObjectDictionary(IDictionary dictionary)
    {
        var normalized = new Dictionary<string, object?>(dictionary.Count, StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in dictionary)
        {
            normalized[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = NormalizeValue(entry.Value);
        }

        return normalized;
    }

    private static IReadOnlyList<object?> NormalizeEnumerable(IEnumerable enumerable)
    {
        var normalized = enumerable is ICollection collection ? new List<object?>(collection.Count) : [];
        foreach (var item in enumerable)
        {
            normalized.Add(NormalizeValue(item));
        }

        return normalized;
    }
}