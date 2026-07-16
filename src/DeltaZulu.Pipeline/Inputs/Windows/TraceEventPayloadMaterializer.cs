using DeltaZulu.Pipeline.Inputs.Etw;
using Microsoft.Diagnostics.Tracing;

namespace DeltaZulu.Pipeline.Inputs.Windows;

internal interface IEtwPayloadMaterializer
{
    EtwPayloadMaterializationResult AddSelected(
        TraceEvent data,
        IReadOnlySet<string>? selectedPayloadFields,
        IDictionary<string, object?> destination);
}

internal sealed class TraceEventPayloadMaterializer : IEtwPayloadMaterializer
{
    public EtwPayloadMaterializationResult AddSelected(
        TraceEvent data,
        IReadOnlySet<string>? selectedPayloadFields,
        IDictionary<string, object?> destination)
    {
        var materialized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var payloadName in EtwPayloadProjection.SelectPayloadNames(TraceEventSourceEventMapper.SafePayloadNames(data), selectedPayloadFields))
        {
            try
            {
                destination[payloadName] = data.PayloadByName(payloadName);
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
