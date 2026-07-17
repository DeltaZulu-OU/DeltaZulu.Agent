namespace DeltaZulu.Pipeline.Inputs.Etw;

/// <summary>
/// Normalizes reader-specific ETW header aliases into the source-native field
/// contract consumed by profiles and source-local correlation plans.
/// </summary>
public static class EtwSourceContractNormalizer
{
    public const string ReaderField = "EtwReader";
    public const string TraceEventReader = "TraceEvent";
    public const string TxTdhReader = "TxTdh";

    public static IReadOnlyDictionary<string, object?> NormalizeTdh(
        IEnumerable<KeyValuePair<string, object?>> fields)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            normalized[field.Key] = field.Value;
        }

        CopyValue(normalized, nameof(NativeEtwEnvelope.ProviderGuid), ["ProviderGuid", "ProviderId", "ProviderID"], ToGuid);
        CopyHeader(normalized, nameof(NativeEtwEnvelope.ProviderName), ["ProviderName", "Provider"], ToStringValue);
        CopyHeader(normalized, nameof(NativeEtwEnvelope.EventId), ["EventId", "EventID", "Id", "ID"], ToInt32);
        CopyHeader(normalized, "EventName", ["EventName", "Event"], ToStringValue);
        CopyHeader(normalized, nameof(NativeEtwEnvelope.Opcode), ["Opcode", "OpcodeValue"], ToInt32);
        CopyHeader(normalized, nameof(NativeEtwEnvelope.OpcodeName), ["OpcodeName", "Opcode"], ToStringValue);
        CopyHeader(normalized, nameof(NativeEtwEnvelope.Version), ["Version", "EventVersion"], ToInt32);
        CopyHeader(normalized, nameof(NativeEtwEnvelope.Task), ["Task", "TaskValue"], ToInt32);
        CopyHeader(normalized, nameof(NativeEtwEnvelope.TaskName), ["TaskName", "Task"], ToStringValue);
        CopyHeader(normalized, "LevelCode", ["LevelCode", "LevelValue", "Level"], ToInt32);
        CopyHeader(normalized, "Level", ["LevelName", "Level"], ToStringValue);
        CopyHeader(normalized, nameof(NativeEtwEnvelope.Keywords), ["Keywords", "Keyword"], ToInt64);
        CopyHeader(normalized, "TimeStamp", ["TimeStamp", "Timestamp", "TimestampUtc"], ToTimestamp);
        CopyHeader(normalized, nameof(NativeEtwEnvelope.ProcessId), ["ProcessId", "ProcessID", "PID"], ToInt32);
        CopyHeader(normalized, nameof(NativeEtwEnvelope.ThreadId), ["ThreadId", "ThreadID", "TID"], ToInt32);
        CopyValue(normalized, nameof(NativeEtwEnvelope.ActivityId), ["ActivityId", "ActivityID"], ToGuid);
        CopyValue(normalized, nameof(NativeEtwEnvelope.RelatedActivityId), ["RelatedActivityId", "RelatedActivityID"], ToGuid);
        CopyHeader(normalized, nameof(NativeEtwEnvelope.Channel), ["Channel", "ChannelId"], ToInt32);
        CopyHeader(normalized, nameof(NativeEtwEnvelope.ProcessorId), ["ProcessorId", "ProcessorNumber", "Cpu"], ToInt32);
        CopyHeader(normalized, nameof(NativeEtwEnvelope.PayloadLength), ["PayloadLength", "EventDataLength"], ToInt32);
        normalized[ReaderField] = TxTdhReader;
        return normalized;
    }

    public static void AddTraceEventProvenance(IDictionary<string, object?> fields) =>
        fields[ReaderField] = TraceEventReader;

    private static void CopyHeader<T>(
        IDictionary<string, object?> fields,
        string canonicalName,
        IEnumerable<string> aliases,
        Func<object?, T?> convert)
        where T : class
    {
        if (TryGetValueIgnoreCase(fields, canonicalName, out var existing) && existing is not null)
        {
            return;
        }

        foreach (var alias in aliases)
        {
            if (!TryGetValueIgnoreCase(fields, alias, out var value))
            {
                continue;
            }

            var converted = convert(value);
            if (converted is not null)
            {
                fields[canonicalName] = converted;
                return;
            }
        }
    }

    private static void CopyHeader(
        IDictionary<string, object?> fields,
        string canonicalName,
        IEnumerable<string> aliases,
        Func<object?, int?> convert) =>
        CopyValue(fields, canonicalName, aliases, convert);

    private static void CopyHeader(
        IDictionary<string, object?> fields,
        string canonicalName,
        IEnumerable<string> aliases,
        Func<object?, long?> convert) =>
        CopyValue(fields, canonicalName, aliases, convert);

    private static void CopyHeader(
        IDictionary<string, object?> fields,
        string canonicalName,
        IEnumerable<string> aliases,
        Func<object?, DateTime?> convert) =>
        CopyValue(fields, canonicalName, aliases, convert);

    private static void CopyValue<T>(
        IDictionary<string, object?> fields,
        string canonicalName,
        IEnumerable<string> aliases,
        Func<object?, T?> convert)
        where T : struct
    {
        if (TryGetValueIgnoreCase(fields, canonicalName, out var existing) && existing is not null)
        {
            return;
        }

        foreach (var alias in aliases)
        {
            if (!TryGetValueIgnoreCase(fields, alias, out var value))
            {
                continue;
            }

            var converted = convert(value);
            if (converted.HasValue)
            {
                fields[canonicalName] = converted.Value;
                return;
            }
        }
    }

    private static bool TryGetValueIgnoreCase(IDictionary<string, object?> fields, string key, out object? value)
    {
        if (fields.TryGetValue(key, out value))
            return true;

        var matchingKey = fields.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (matchingKey != null)
            return fields.TryGetValue(matchingKey, out value);

        return false;
    }

    private static Guid? ToGuid(object? value)
    {
        if (value is Guid guid)
        {
            return guid;
        }

        return Guid.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    }

    private static string? ToStringValue(object? value) => value?.ToString();

    private static int? ToInt32(object? value)
    {
        try
        {
            return value is null ? null : Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static long? ToInt64(object? value)
    {
        try
        {
            return value is null ? null : Convert.ToInt64(value);
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? ToTimestamp(object? value) => value switch
    {
        DateTime timestamp => timestamp,
        DateTimeOffset timestamp => timestamp.UtcDateTime,
        _ when DateTime.TryParse(value?.ToString(), out var timestamp) => timestamp,
        _ => null
    };
}
