using Microsoft.Diagnostics.Tracing;

namespace DeltaZulu.Pipeline.Inputs.Windows;

internal interface IEtwPayloadMaterializer
{
    void AddSelected(
        TraceEvent data,
        IReadOnlySet<string>? selectedPayloadFields,
        IDictionary<string, object?> destination);
}

internal sealed class TraceEventPayloadMaterializer : IEtwPayloadMaterializer
{
    public void AddSelected(
        TraceEvent data,
        IReadOnlySet<string>? selectedPayloadFields,
        IDictionary<string, object?> destination)
    {
        var materializeAll = selectedPayloadFields is null || selectedPayloadFields.Count == 0;

        foreach (var payloadName in data.PayloadNames)
        {
            if (!materializeAll && !selectedPayloadFields!.Contains(payloadName))
            {
                continue;
            }

            try
            {
                destination[payloadName] = data.PayloadByName(payloadName);
            }
            catch
            {
                // Some providers expose payload slots that cannot be decoded on every event version.
                // Keep the envelope and any readable payload fields instead of dropping the event.
            }
        }
    }
}
