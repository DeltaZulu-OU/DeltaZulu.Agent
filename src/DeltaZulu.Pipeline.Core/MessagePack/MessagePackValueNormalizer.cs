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

    private static DeliveryBatch NormalizeBatch(DeliveryBatch batch) => batch with {
        Records = batch.Records.Select(NormalizeRecord).ToList()
    };

    private static DeliveryRecord NormalizeRecord(DeliveryRecord record) => record with {
        Record = NormalizeResourceOutput(record.Record)
    };

    private static ResourceOutputRecord NormalizeResourceOutput(ResourceOutputRecord record) => record with {
        Metadata = NormalizeStringDictionary(record.Metadata),
        Event = NormalizeStringDictionary(record.Event),
        Enrichment = record.Enrichment is null ? null : NormalizeStringDictionary(record.Enrichment)
    };

    private static IReadOnlyDictionary<string, object?> NormalizeStringDictionary(IReadOnlyDictionary<string, object?> dictionary) =>
        dictionary.ToDictionary(k => k.Key, v => NormalizeValue(v.Value), StringComparer.OrdinalIgnoreCase);

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
        IDictionary<string, object?> dictionary => dictionary.ToDictionary(k => k.Key, v => NormalizeValue(v.Value), StringComparer.OrdinalIgnoreCase),
        IDictionary dictionary => NormalizeObjectDictionary(dictionary),
        byte[] bytes => bytes,
        IEnumerable enumerable when value is not string => NormalizeEnumerable(enumerable),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private static object? NormalizeJsonElement(JsonElement element) => element.ValueKind switch {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => NormalizeJsonElement(p.Value), StringComparer.OrdinalIgnoreCase),
        JsonValueKind.Array => element.EnumerateArray().Select(NormalizeJsonElement).ToList(),
        JsonValueKind.String => element.TryGetDateTimeOffset(out var timestamp) ? timestamp.ToString("O", CultureInfo.InvariantCulture) : element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.TryGetDouble(out var number) ? number : element.GetRawText(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.GetRawText()
    };

    private static IReadOnlyDictionary<string, object?> NormalizeObjectDictionary(IDictionary dictionary)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in dictionary)
        {
            normalized[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = NormalizeValue(entry.Value);
        }

        return normalized;
    }

    private static IReadOnlyList<object?> NormalizeEnumerable(IEnumerable enumerable)
    {
        var normalized = new List<object?>();
        foreach (var item in enumerable)
        {
            normalized.Add(NormalizeValue(item));
        }

        return normalized;
    }
}