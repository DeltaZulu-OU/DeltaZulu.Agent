using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Pipeline.Inputs.Etw;

public static class EtwPayloadProjection
{
    private static readonly EtwResourceOptionsAdapter OptionsAdapter = new();

    public static IReadOnlySet<string>? BuildSelectedPayloadFields(ResourceDescriptor? resource)
    {
        if (resource is null)
        {
            return null;
        }

        var payloadFields = OptionsAdapter.Adapt(resource).PayloadFields;
        return payloadFields.Count == 0
            ? null
            : payloadFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> SelectMissingPayloadFields(
        IReadOnlySet<string>? selectedPayloadFields,
        EtwPayloadMaterializationResult payloadMaterialization)
    {
        if (selectedPayloadFields is null || selectedPayloadFields.Count == 0)
        {
            yield break;
        }

        foreach (var payloadField in selectedPayloadFields)
        {
            if (!payloadMaterialization.MaterializedPayloadFields.Contains(payloadField) &&
                !payloadMaterialization.FailedPayloadFields.Contains(payloadField))
            {
                yield return payloadField;
            }
        }
    }

    public static IEnumerable<string> SelectPayloadNames(
            IEnumerable<string> payloadNames,
        IReadOnlySet<string>? selectedPayloadFields)
    {
        var materializeAll = selectedPayloadFields is null || selectedPayloadFields.Count == 0;

        foreach (var payloadName in payloadNames)
        {
            if (materializeAll || selectedPayloadFields!.Contains(payloadName))
            {
                yield return payloadName;
            }
        }
    }
}
