using System.Collections;
using System.Globalization;
using System.Text.Json;
using DeltaZulu.Forward;

namespace DeltaZulu.Pipeline.Core.Delivery;

/// <summary>
/// Unwraps <see cref="JsonElement" /> values left behind by the durable-buffer JSON
/// round trip back into plain CLR values before a <see cref="ForwardLogBatch" /> reaches
/// <see cref="ForwardLogBatchCodec.Encode" />. Final KQL-scalar validation/normalization
/// (widening, decimal/Guid/TimeSpan handling, etc.) is owned by DeltaZulu.Forward itself;
/// this only handles the one shape Forward's normalizer cannot: <see cref="JsonElement" />.
/// </summary>
internal static class ForwardFieldValueNormalizer
{
    public static ForwardLogBatch Normalize(ForwardLogBatch batch)
    {
        List<ForwardLogRecord>? normalizedRecords = null;
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
                normalizedRecords = new List<ForwardLogRecord>(batch.Records.Count);
                for (var copyIndex = 0; copyIndex < index; copyIndex++)
                {
                    normalizedRecords.Add(batch.Records[copyIndex]);
                }

                normalizedRecords.Add(normalized);
            }
        }

        return normalizedRecords is null ? batch : batch with { Records = normalizedRecords };
    }

    private static ForwardLogRecord NormalizeRecord(ForwardLogRecord record)
    {
        var fields = NormalizeFields(record.Fields);
        return ReferenceEquals(fields, record.Fields) ? record : record with { Fields = fields };
    }

    private static IReadOnlyDictionary<string, object?> NormalizeFields(IReadOnlyDictionary<string, object?> fields)
    {
        Dictionary<string, object?>? normalized = null;
        foreach (var pair in fields)
        {
            var normalizedValue = NormalizeValue(pair.Value);
            if (normalized is not null)
            {
                normalized[pair.Key] = normalizedValue;
            }
            else if (!ReferenceEquals(normalizedValue, pair.Value))
            {
                normalized = new Dictionary<string, object?>(fields.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var copy in fields)
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

        return normalized ?? fields;
    }

    private static object? NormalizeValue(object? value) => value switch {
        JsonElement element => NormalizeJsonElement(element),
        IReadOnlyDictionary<string, object?> dictionary => NormalizeFields(dictionary),
        IEnumerable enumerable when value is not string => NormalizeEnumerable(enumerable),
        _ => value
    };

    private static IReadOnlyList<object?> NormalizeEnumerable(IEnumerable enumerable)
    {
        var normalized = enumerable is ICollection collection ? new List<object?>(collection.Count) : [];
        foreach (var item in enumerable)
        {
            normalized.Add(NormalizeValue(item));
        }

        return normalized;
    }

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
}
