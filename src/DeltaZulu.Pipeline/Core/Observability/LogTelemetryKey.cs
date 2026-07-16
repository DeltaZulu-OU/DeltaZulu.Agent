using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Core.Observability;

public sealed record LogTelemetryKey(string SourceType, string Channel, string? Provider, int? EventId)
{
    public static LogTelemetryKey FromSourceEvent(SourceEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var metadata = source.Metadata;
        return new LogTelemetryKey(
            metadata.SourceType,
            metadata.SourceName,
            GetString(source.Fields, "Provider", "ProviderName", "ProviderNameRaw"),
            GetInt(source.Fields, "EventId", "EventID", "Id"));
    }

    public static LogTelemetryKey FromOutputRecord(ResourceOutputRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new LogTelemetryKey(
            GetString(record.Metadata, "sourceType") ?? string.Empty,
            GetString(record.Metadata, "sourceName", "channel") ?? string.Empty,
            GetString(record.Event, "Provider", "ProviderName", "ProviderNameRaw"),
            GetInt(record.Event, "EventId", "EventID", "Id"));
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && value is not null)
            {
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static int? GetInt(IReadOnlyDictionary<string, object?> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!values.TryGetValue(key, out var value) || value is null)
            {
                continue;
            }

            if (value is int integer)
            {
                return integer;
            }

            if (int.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
