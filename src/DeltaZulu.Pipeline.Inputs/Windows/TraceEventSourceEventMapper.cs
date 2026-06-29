using Microsoft.Diagnostics.Tracing;

namespace DeltaZulu.Pipeline.Inputs.Windows;

internal static class TraceEventSourceEventMapper
{
    public static IReadOnlyDictionary<string, object?> ToDictionary(TraceEvent data)
    {
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProviderName"] = data.ProviderName,
            ["EventId"] = (int)data.ID,
            ["EventName"] = data.EventName,
            ["OpcodeName"] = data.OpcodeName,
            ["Level"] = data.Level.ToString(),
            ["Keywords"] = data.Keywords,
            ["TimeStamp"] = data.TimeStamp,
            ["ProcessId"] = data.ProcessID,
            ["ThreadId"] = data.ThreadID,
            ["ActivityId"] = data.ActivityID
        };

        foreach (var payloadName in data.PayloadNames)
        {
            try
            {
                fields[payloadName] = data.PayloadByName(payloadName);
            }
            catch
            {
                // Some providers expose payload slots that cannot be decoded on every event version.
                // Keep the envelope and any readable payload fields instead of dropping the event.
            }
        }

        return fields.AsReadOnly();
    }
}
