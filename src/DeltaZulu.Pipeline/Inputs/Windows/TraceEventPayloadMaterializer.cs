using DeltaZulu.Pipeline.Inputs.Etw;
using Microsoft.Diagnostics.Tracing;

namespace DeltaZulu.Pipeline.Inputs.Windows;

internal interface IEtwPayloadMaterializer
{
    EtwPayloadMaterializationResult AddSelected(
        TraceEvent data,
        string[] payloadNames,
        IReadOnlySet<string>? selectedPayloadFields,
        IDictionary<string, object?> destination);
}

internal sealed class TraceEventPayloadMaterializer : IEtwPayloadMaterializer
{
    public EtwPayloadMaterializationResult AddSelected(
        TraceEvent data,
        string[] payloadNames,
        IReadOnlySet<string>? selectedPayloadFields,
        IDictionary<string, object?> destination)
    {
        var materialized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var materializeAll = selectedPayloadFields is null || selectedPayloadFields.Count == 0;

        // Read payload values by index (data.PayloadValue(i)) rather than by name
        // (data.PayloadByName(name)). TraceEvent's PayloadByName re-walks the
        // PayloadNames array with an O(n) string scan on every call, which turns an
        // n-field event into an O(n^2) materialization. We already have the index
        // from enumerating payloadNames, so this is a single O(n) pass instead.
        for (var i = 0; i < payloadNames.Length; i++)
        {
            var payloadName = payloadNames[i];
            if (!materializeAll && !selectedPayloadFields!.Contains(payloadName))
            {
                continue;
            }

            try
            {
                destination[payloadName] = data.PayloadValue(i);
                materialized.Add(payloadName);
            }
            catch
            {
                failed.Add(payloadName);
                // Some providers expose payload slots that cannot be decoded on every event version.
                // Keep the envelope and any readable payload fields instead of dropping the event.
            }
        }

        return new EtwPayloadMaterializationResult(materialized, failed);
    }
}
