using Microsoft.Diagnostics.Tracing;

namespace DeltaZulu.Pipeline.Inputs.Windows;

internal interface IEtwPayloadMaterializer
{
    IReadOnlyDictionary<string, object?> MaterializeSelected(
        TraceEvent data,
        IReadOnlySet<string>? selectedPayloadFields);
}

internal sealed class TraceEventPayloadMaterializer : IEtwPayloadMaterializer
{
    public IReadOnlyDictionary<string, object?> MaterializeSelected(
        TraceEvent data,
        IReadOnlySet<string>? selectedPayloadFields)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var materializeAll = selectedPayloadFields is null || selectedPayloadFields.Count == 0;

        foreach (var payloadName in data.PayloadNames)
        {
            if (!materializeAll && !selectedPayloadFields!.Contains(payloadName))
            {
                continue;
            }

            try
            {
                payload[payloadName] = data.PayloadByName(payloadName);
            }
            catch
            {
                // Some providers expose payload slots that cannot be decoded on every event version.
                // Keep the envelope and any readable payload fields instead of dropping the event.
            }
        }

        return payload;
    }
}
